using System.Security.Cryptography;
using System.Text;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>SQL syntax helpers; names are identifiers, while backup paths and MOVE values are parameters.</summary>
public static class BackupSql
{
    public static string Identifier(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || databaseName.Length > 128 || databaseName.Any(char.IsControl))
            throw new ArgumentException("اسم قاعدة البيانات غير صالح.", nameof(databaseName));
        if (new[] { "master", "model", "msdb", "tempdb" }.Contains(databaseName, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("لا يمكن نسخ أو استبدال قواعد SQL Server النظامية من البرنامج.", nameof(databaseName));
        return "[" + databaseName.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    public static string BackupCommand(string databaseName) =>
        $"BACKUP DATABASE {Identifier(databaseName)} TO DISK = @path WITH COPY_ONLY, INIT, CHECKSUM, NO_COMPRESSION, STATS = 10";

    public static string RestoreCommand(string databaseName, int backupPosition, int fileCount)
    {
        if (backupPosition <= 0) throw new ArgumentOutOfRangeException(nameof(backupPosition));
        if (fileCount is < 2 or > 128) throw new ArgumentOutOfRangeException(nameof(fileCount));
        var moves = string.Join(", ", Enumerable.Range(0, fileCount).Select(i => $"MOVE @logical{i} TO @target{i}"));
        return $"RESTORE DATABASE {Identifier(databaseName)} FROM DISK = @path WITH FILE = {backupPosition}, {moves}, REPLACE, STATS = 10";
    }

    public static void ValidateBackupSource(string targetDatabase, string sourceDatabase, int backupType, bool isDamaged)
    {
        Identifier(targetDatabase);
        if (!string.Equals(targetDatabase, sourceDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"النسخة تخص قاعدة '{sourceDatabase}' بينما الاتصال الحالي بقاعدة '{targetDatabase}'. لم تُستبدل البيانات.");
        if (backupType != 1 || isDamaged)
            throw new InvalidOperationException("اختر نسخة كاملة وسليمة لقاعدة البيانات، وليس نسخة سجل أو نسخة تفاضلية.");
    }

    public static string SafeFileStem(string databaseName)
    {
        Identifier(databaseName);
        var stem = new string(databaseName.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').Take(60).ToArray());
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(databaseName)))[..8];
        return stem + "-" + suffix;
    }

    public static string CombineServerPath(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("تعذر تحديد مجلد بيانات SQL Server على الخادم.");
        var separator = directory.Contains('\\') || (directory.Length > 1 && directory[1] == ':') ? '\\' : '/';
        return directory.TrimEnd('\\', '/') + separator + fileName;
    }
}
