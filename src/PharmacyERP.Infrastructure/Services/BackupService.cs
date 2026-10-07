using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Backup;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>
/// Runs native SQL Server BACKUP DATABASE / RESTORE DATABASE statements
/// directly, rather than any custom serialization format — the resulting
/// .bak files are standard SQL Server backups, restorable with SSMS or any
/// other tool even without this application, which is what actually matters
/// for disaster recovery.
/// </summary>
public class BackupService : IBackupService
{
    private readonly string _connectionString;
    private readonly string _databaseName;

    public BackupService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        var builder = new SqlConnectionStringBuilder(_connectionString);
        _databaseName = builder.InitialCatalog;

        var configuredFolder = configuration["ApplicationSettings:BackupFolder"];
        BackupFolder = string.IsNullOrWhiteSpace(configuredFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PharmacyERP", "Backups")
            : configuredFolder;

        Directory.CreateDirectory(BackupFolder);
    }

    public string BackupFolder { get; }

    public Task<List<BackupFileDto>> GetBackupHistoryAsync(CancellationToken cancellationToken = default)
    {
        var files = Directory.Exists(BackupFolder)
            ? Directory.GetFiles(BackupFolder, "*.bak")
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Select(f => new BackupFileDto
                {
                    FileName = f.Name,
                    FullPath = f.FullName,
                    SizeBytes = f.Length,
                    CreatedAtUtc = f.CreationTimeUtc
                })
                .ToList()
            : new List<BackupFileDto>();

        return Task.FromResult(files);
    }

    public async Task<Result<BackupFileDto>> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var fileName = $"{_databaseName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";
        var fullPath = Path.Combine(BackupFolder, fileName);

        try
        {
            // A server-side "master" connection (not scoped to the pharmacy database itself)
            // is required to issue BACKUP DATABASE — connecting to the target database directly
            // and running BACKUP against itself is entirely normal for SQL Server, so the same
            // connection string works here without modification.
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"BACKUP DATABASE [{_databaseName}] TO DISK = @path WITH FORMAT, INIT, COMPRESSION, STATS = 10";
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 600 };
            command.Parameters.AddWithValue("@path", fullPath);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<BackupFileDto>.Failure($"فشل إنشاء النسخة الاحتياطية: {ex.Message}");
        }

        var info = new FileInfo(fullPath);
        return Result<BackupFileDto>.Success(new BackupFileDto
        {
            FileName = fileName,
            FullPath = fullPath,
            SizeBytes = info.Length,
            CreatedAtUtc = info.CreationTimeUtc
        });
    }

    public async Task<Result> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupFilePath))
            return Result.Failure("ملف النسخة الاحتياطية غير موجود.");

        try
        {
            // Restoring requires the database to have no other open connections. Switching it to
            // SINGLE_USER with ROLLBACK IMMEDIATE forcibly drops every other session (including
            // this application's own connection pool) — the caller is responsible for restarting
            // the application immediately after a successful restore.
            var masterConnectionString = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync(cancellationToken);

            async Task ExecAsync(string sql)
            {
                await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 600 };
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await ExecAsync($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");

            await using (var restoreCmd = new SqlCommand(
                $"RESTORE DATABASE [{_databaseName}] FROM DISK = @path WITH REPLACE, STATS = 10", connection)
            { CommandTimeout = 1200 })
            {
                restoreCmd.Parameters.AddWithValue("@path", backupFilePath);
                await restoreCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await ExecAsync($"ALTER DATABASE [{_databaseName}] SET MULTI_USER");
        }
        catch (Exception ex)
        {
            return Result.Failure($"فشلت عملية الاستعادة: {ex.Message}");
        }

        return Result.Success();
    }

    public Task<Result> DeleteBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.Failure($"تعذر حذف الملف: {ex.Message}"));
        }
    }
}
