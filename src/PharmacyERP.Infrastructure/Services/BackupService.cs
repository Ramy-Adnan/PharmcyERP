using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Backup;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>Native SQL Server backups, including Express. Restore validates before disconnecting users.</summary>
public class BackupService : IBackupService
{
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly SemaphoreSlim _operation = new(1, 1);

    public BackupService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("لم يُضبط اتصال قاعدة البيانات.");
        var builder = new SqlConnectionStringBuilder(_connectionString);
        _databaseName = builder.InitialCatalog;
        BackupSql.Identifier(_databaseName);
        var configuredFolder = configuration["ApplicationSettings:BackupFolder"];
        BackupFolder = string.IsNullOrWhiteSpace(configuredFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PharmacyERP", "Backups")
            : Path.GetFullPath(configuredFolder);
        Directory.CreateDirectory(BackupFolder);
    }

    public string BackupFolder { get; }

    public Task<List<BackupFileDto>> GetBackupHistoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Directory.EnumerateFiles(BackupFolder, "*.bak")
            .Select(path => new FileInfo(path)).OrderByDescending(f => f.CreationTimeUtc).Select(ToDto).ToList());
    }

    public async Task<Result<BackupFileDto>> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            var fullPath = Path.Combine(BackupFolder, $"{BackupSql.SafeFileStem(_databaseName)}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.bak");
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(BackupSql.BackupCommand(_databaseName), connection) { CommandTimeout = 1200 };
            command.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = fullPath;
            await command.ExecuteNonQueryAsync(cancellationToken);
            if (!File.Exists(fullPath))
                return Result<BackupFileDto>.Failure("أنشأ SQL Server النسخة لكن الملف غير متاح لهذا الكمبيوتر. اضبط مجلد النسخ على مسار يراه التطبيق وخدمة SQL Server، أو مشاركة شبكة بصلاحيات مناسبة.");
            return Result<BackupFileDto>.Success(ToDto(new FileInfo(fullPath)));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Result<BackupFileDto>.Failure($"فشل إنشاء النسخة الاحتياطية. تأكد أن خدمة SQL Server تملك صلاحية الكتابة في مجلد النسخ. {ex.Message}");
        }
        finally { _operation.Release(); }
    }

    public async Task<Result> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFilePath) || !string.Equals(Path.GetExtension(backupFilePath), ".bak", StringComparison.OrdinalIgnoreCase))
            return Result.Failure("اختر ملف نسخة SQL Server بامتداد .bak موجوداً على مسار يستطيع الخادم قراءته.");
        await _operation.WaitAsync(cancellationToken);
        var singleUser = false;
        string? failure = null;
        try
        {
            await using var connection = CreateMasterConnection();
            await connection.OpenAsync(cancellationToken);
            var position = await ReadBackupPositionAsync(connection, backupFilePath, cancellationToken);
            var files = await ReadBackupFilesAsync(connection, backupFilePath, position, cancellationToken);
            var targets = await ReadTargetPathsAsync(connection, files, cancellationToken);
            await ExecuteWithPathAsync(connection, $"RESTORE VERIFYONLY FROM DISK = @path WITH FILE = {position}", backupFilePath, cancellationToken);

            var exists = await DatabaseExistsAsync(connection, cancellationToken);
            SqlConnection.ClearAllPools();
            if (exists)
            {
                await ExecuteAsync(connection, $"ALTER DATABASE {BackupSql.Identifier(_databaseName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE", cancellationToken);
                singleUser = true;
            }
            await using var command = new SqlCommand(BackupSql.RestoreCommand(_databaseName, position, files.Count), connection) { CommandTimeout = 1800 };
            command.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = Path.GetFullPath(backupFilePath);
            for (var i = 0; i < files.Count; i++)
            {
                command.Parameters.Add($"@logical{i}", SqlDbType.NVarChar, 128).Value = files[i].LogicalName;
                command.Parameters.Add($"@target{i}", SqlDbType.NVarChar, 4000).Value = targets[i];
            }
            await command.ExecuteNonQueryAsync(cancellationToken);
            // RESTORE can inherit SINGLE_USER from a source backup for a previously absent database.
            singleUser = true;
        }
        catch (OperationCanceledException) { failure = "أُلغيت الاستعادة. تحقق من حالة قاعدة البيانات قبل متابعة العمل."; }
        catch (Exception ex) { failure = $"فشلت عملية الاستعادة: {ex.Message}"; }
        finally
        {
            if (singleUser)
            {
                try
                {
                    // Do not use the cancelled token: restoring multi-user access is mandatory cleanup.
                    await using var cleanup = CreateMasterConnection();
                    await cleanup.OpenAsync(CancellationToken.None);
                    await ExecuteAsync(cleanup, $"ALTER DATABASE {BackupSql.Identifier(_databaseName)} SET MULTI_USER WITH ROLLBACK IMMEDIATE", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    failure = (failure is null ? "تمت الاستعادة لكن " : failure + " ") + "تعذر إعادة قاعدة البيانات إلى MULTI_USER. يلزم تدخل مسؤول SQL Server: " + ex.Message;
                }
            }
            SqlConnection.ClearAllPools();
            _operation.Release();
        }
        return failure is null ? Result.Success() : Result.Failure(failure);
    }

    public async Task<Result> DeleteBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        await _operation.WaitAsync(cancellationToken);
        try
        {
            var path = Path.GetFullPath(backupFilePath);
            if (!string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(BackupFolder), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(path), ".bak", StringComparison.OrdinalIgnoreCase))
                return Result.Failure("يمكن حذف ملفات .bak من مجلد النسخ المحدد فقط.");
            if (File.Exists(path)) File.Delete(path);
            return Result.Success();
        }
        catch (Exception ex) { return Result.Failure($"تعذر حذف الملف: {ex.Message}"); }
        finally { _operation.Release(); }
    }

    private SqlConnection CreateMasterConnection() => new(new SqlConnectionStringBuilder(_connectionString)
        { InitialCatalog = "master", Pooling = false }.ConnectionString);

    private async Task<int> ReadBackupPositionAsync(SqlConnection connection, string path, CancellationToken token)
    {
        await using var command = new SqlCommand("RESTORE HEADERONLY FROM DISK = @path", connection) { CommandTimeout = 600 };
        command.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = Path.GetFullPath(path);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var database = Convert.ToString(reader["DatabaseName"]) ?? string.Empty;
            var type = Convert.ToInt32(reader["BackupType"]);
            if (!string.Equals(database, _databaseName, StringComparison.OrdinalIgnoreCase) || type != 1) continue;
            var damaged = reader["IsDamaged"] != DBNull.Value && Convert.ToBoolean(reader["IsDamaged"]);
            BackupSql.ValidateBackupSource(_databaseName, database, type, damaged);
            return Convert.ToInt32(reader["Position"]);
        }
        throw new InvalidOperationException($"الملف لا يحتوي نسخة كاملة لقاعدة '{_databaseName}'. لم تُستبدل البيانات.");
    }

    private static async Task<List<BackupFile>> ReadBackupFilesAsync(SqlConnection connection, string path, int position, CancellationToken token)
    {
        await using var command = new SqlCommand($"RESTORE FILELISTONLY FROM DISK = @path WITH FILE = {position}", connection) { CommandTimeout = 600 };
        command.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = Path.GetFullPath(path);
        await using var reader = await command.ExecuteReaderAsync(token);
        var files = new List<BackupFile>();
        while (await reader.ReadAsync(token))
        {
            var type = Convert.ToString(reader["Type"]);
            if (type is not ("D" or "L")) throw new InvalidOperationException("تحتوي النسخة ملفات FILESTREAM أو أنواعاً خاصة؛ استخدم SQL Server Management Studio لاستعادتها.");
            files.Add(new BackupFile(Convert.ToString(reader["LogicalName"])!, type));
        }
        if (files.Count is < 2 or > 128 || !files.Any(f => f.Type == "D") || !files.Any(f => f.Type == "L"))
            throw new InvalidOperationException("قائمة ملفات قاعدة البيانات غير صالحة.");
        return files;
    }

    private async Task<List<string>> ReadTargetPathsAsync(SqlConnection connection, List<BackupFile> files, CancellationToken token)
    {
        var existingData = new Queue<string>();
        var existingLog = new Queue<string>();
        await using (var command = new SqlCommand("SELECT type, physical_name FROM sys.master_files WHERE database_id = DB_ID(@database) AND type IN (0,1) ORDER BY file_id", connection))
        {
            command.Parameters.Add("@database", SqlDbType.NVarChar, 128).Value = _databaseName;
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                (reader.GetByte(0) == 0 ? existingData : existingLog).Enqueue(reader.GetString(1));
        }
        string? dataDirectory;
        string? logDirectory;
        await using (var command = new SqlCommand("SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultDataPath')), CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultLogPath'))", connection))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            await reader.ReadAsync(token);
            dataDirectory = reader.IsDBNull(0) ? null : reader.GetString(0);
            logDirectory = reader.IsDBNull(1) ? dataDirectory : reader.GetString(1);
        }
        var result = new List<string>();
        var dataIndex = 0;
        var logIndex = 0;
        foreach (var file in files)
        {
            var queue = file.Type == "D" ? existingData : existingLog;
            var index = file.Type == "D" ? ++dataIndex : ++logIndex;
            var extension = file.Type == "L" ? ".ldf" : index == 1 ? ".mdf" : ".ndf";
            result.Add(queue.Count > 0 ? queue.Dequeue() : BackupSql.CombineServerPath(
                (file.Type == "D" ? dataDirectory : logDirectory) ?? throw new InvalidOperationException("تعذر تحديد مسارات ملفات SQL Server على الوجهة."),
                BackupSql.SafeFileStem(_databaseName) + (file.Type == "L" ? "_log" : "_data") + index + extension));
        }
        return result;
    }

    private async Task<bool> DatabaseExistsAsync(SqlConnection connection, CancellationToken token)
    {
        await using var command = new SqlCommand("SELECT DB_ID(@database)", connection);
        command.Parameters.Add("@database", SqlDbType.NVarChar, 128).Value = _databaseName;
        return await command.ExecuteScalarAsync(token) is not (null or DBNull);
    }

    private static async Task ExecuteWithPathAsync(SqlConnection connection, string sql, string path, CancellationToken token)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 1200 };
        command.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = Path.GetFullPath(path);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 600 };
        await command.ExecuteNonQueryAsync(token);
    }

    private static BackupFileDto ToDto(FileInfo info) => new()
        { FileName = info.Name, FullPath = info.FullName, SizeBytes = info.Length, CreatedAtUtc = info.CreationTimeUtc };
    private sealed record BackupFile(string LogicalName, string Type);
}
