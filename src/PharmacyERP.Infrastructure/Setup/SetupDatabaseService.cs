using Microsoft.Data.SqlClient;

namespace PharmacyERP.Infrastructure.Setup;

public enum DatabaseSetupMode { UseExisting, CreateNew }

public sealed record SetupDatabaseStatus(bool DatabaseExists, bool IsPharmacyDatabase, bool IsEmpty, bool HasAdministrator);

/// <summary>Preflight checks never migrate, overwrite, attach or delete a database.</summary>
public sealed class SetupDatabaseService
{
    public static SqlConnectionStringBuilder ValidateConnectionString(string connectionString)
    {
        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("إعدادات الاتصال غير صالحة. أعد إدخال اسم الخادم والقاعدة."); }
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource.Length > 256)
            throw new InvalidOperationException("أدخل اسم خادم SQL Server.");
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog) || builder.InitialCatalog.Length > 128 || builder.InitialCatalog.Any(char.IsControl))
            throw new InvalidOperationException("أدخل اسم قاعدة بيانات من 1 إلى 128 حرفاً دون أسطر أو محارف تحكم.");
        if (new[] { "master", "model", "msdb", "tempdb" }.Contains(builder.InitialCatalog, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("لا يمكن استخدام قاعدة بيانات نظام SQL Server كقاعدة للصيدلية.");
        if (!string.IsNullOrWhiteSpace(builder.AttachDBFilename))
            throw new InvalidOperationException("استخدم قاعدة SQL Server مثبتة، وليس إرفاق ملف MDF من إعدادات الاتصال.");
        if (!builder.IntegratedSecurity && (string.IsNullOrWhiteSpace(builder.UserID) || string.IsNullOrEmpty(builder.Password)))
            throw new InvalidOperationException("أدخل اسم مستخدم SQL Server وكلمة مروره، أو اختر مصادقة Windows.");
        builder.PersistSecurityInfo = false;
        builder.ConnectTimeout = 10;
        return builder;
    }

    public async Task<SetupDatabaseStatus> InspectAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var builder = ValidateConnectionString(connectionString);
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync(cancellationToken);
        await using (var exists = new SqlCommand("SELECT DB_ID(@database)", master) { CommandTimeout = 15 })
        {
            exists.Parameters.AddWithValue("@database", database);
            if (await exists.ExecuteScalarAsync(cancellationToken) is null or DBNull)
                return new(false, false, false, false);
        }

        builder.InitialCatalog = database;
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        const string inspectSql = """
            SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.objects WHERE is_ms_shipped = 0 AND [type] IN ('U','V','P','FN','IF','TF','SN','SO')
                    AND [object_id] <> ISNULL(OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U'), -1)) THEN 0 ELSE 1 END,
                CASE WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
                    AND COL_LENGTH(N'dbo.Users', N'Username') IS NOT NULL
                    AND COL_LENGTH(N'dbo.Users', N'PasswordHash') IS NOT NULL
                    AND COL_LENGTH(N'dbo.Users', N'IsDeleted') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL
                    AND OBJECT_ID(N'dbo.Branches', N'U') IS NOT NULL THEN 1 ELSE 0 END,
                CASE WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL THEN 1 ELSE 0 END
            """;
        bool empty, candidate, hasHistory;
        await using (var command = new SqlCommand(inspectSql, connection) { CommandTimeout = 15 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            empty = reader.GetInt32(0) == 1;
            candidate = reader.GetInt32(1) == 1;
            hasHistory = reader.GetInt32(2) == 1;
        }
        if (empty && hasHistory)
        {
            // EF creates the history table before executing the first migration.
            // A failed, rolled-back first migration can leave only this empty
            // table behind. Retrying must not require deleting the database.
            await using var historyCount = new SqlCommand("SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory]", connection) { CommandTimeout = 15 };
            empty = Convert.ToInt32(await historyCount.ExecuteScalarAsync(cancellationToken)) == 0;
        }
        if (!candidate) return new(true, false, empty, false);

        // A similarly named Users table alone must never qualify an unrelated database.
        await using (var migration = new SqlCommand("SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = @initialMigration", connection) { CommandTimeout = 15 })
        {
            migration.Parameters.AddWithValue("@initialMigration", "20261004214902_InitialCreate");
            if (Convert.ToInt32(await migration.ExecuteScalarAsync(cancellationToken)) == 0)
                return new(true, false, false, false);
        }
        await using var admin = new SqlCommand("SELECT COUNT(*) FROM [dbo].[Users] WHERE [Username] = N'admin' AND [IsDeleted] = 0", connection) { CommandTimeout = 15 };
        return new(true, true, false, Convert.ToInt32(await admin.ExecuteScalarAsync(cancellationToken)) > 0);
    }

    public async Task<SetupDatabaseStatus> CheckAsync(string connectionString, DatabaseSetupMode mode, CancellationToken cancellationToken = default)
    {
        var status = await InspectAsync(connectionString, cancellationToken);
        ValidateSelection(status, mode);
        return status;
    }

    public static void ValidateSelection(SetupDatabaseStatus status, DatabaseSetupMode mode)
    {
        if (mode == DatabaseSetupMode.CreateNew && status.DatabaseExists)
            throw new InvalidOperationException("توجد قاعدة بهذا الاسم. اختر الاتصال بقاعدة موجودة أو غيّر الاسم لإنشاء قاعدة أخرى؛ لن تُمسح القاعدة الحالية.");
        if (mode == DatabaseSetupMode.UseExisting && !status.DatabaseExists)
            throw new InvalidOperationException("القاعدة غير موجودة أو حساب SQL لا يراها. صحح الاسم والصلاحيات، أو اختر إنشاء قاعدة جديدة.");
        if (mode == DatabaseSetupMode.UseExisting && !status.IsPharmacyDatabase && !status.IsEmpty)
            throw new InvalidOperationException("هذه القاعدة تحتوي بيانات لا يُعرّفها النظام كقاعدة Pharmacy ERP. لن يتم تعديلها. اختر قاعدة الصيدلية الصحيحة.");
    }

    public async Task<SetupDatabaseStatus> PrepareAsync(string connectionString, DatabaseSetupMode mode, CancellationToken cancellationToken = default)
    {
        var status = await CheckAsync(connectionString, mode, cancellationToken);
        if (mode == DatabaseSetupMode.UseExisting) return status;
        var builder = ValidateConnectionString(connectionString);
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        // CREATE fails if another process has created the name in the meantime.
        // No DROP, RESTORE, REPLACE or destructive fallback is issued.
        await using var create = new SqlCommand($"CREATE DATABASE {QuoteDatabaseIdentifier(database)}", connection) { CommandTimeout = 60 };
        await create.ExecuteNonQueryAsync(cancellationToken);
        return new(true, false, true, false);
    }

    public static string QuoteDatabaseIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            throw new InvalidOperationException("اسم القاعدة غير صالح.");
        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    public static string SafeFailureMessage(Exception exception) => exception switch
    {
        SqlException sql when sql.Number is 18456 or 18452 => "فشلت مصادقة SQL Server. تحقق من نوع المصادقة واسم المستخدم وكلمة المرور.",
        SqlException sql when sql.Number is 4060 or 916 or 229 or 262 => "حساب الاتصال لا يملك الصلاحيات اللازمة للقاعدة. راجع صلاحيات SQL Server.",
        SqlException sql when sql.Number is -2 or 53 or 40 => "تعذر الوصول إلى SQL Server. تحقق من اسم الخادم وتشغيل الخدمة والاتصال بالشبكة.",
        SqlException sql => $"تعذر إكمال إعداد SQL Server (رمز {sql.Number}). تحقق من الخدمة والصلاحيات ثم أعد المحاولة.",
        InvalidOperationException known => known.Message,
        _ => "تعذر حفظ إعدادات الاتصال. تحقق من صلاحيات حساب Windows وSQL Server ثم أعد المحاولة."
    };
}
