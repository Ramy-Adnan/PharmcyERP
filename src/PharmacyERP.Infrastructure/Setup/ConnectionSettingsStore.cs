using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PharmacyERP.Infrastructure.Setup;

public interface ISetupSecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public sealed class WindowsSetupSecretProtector : ISetupSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PharmacyERP.DatabaseConnection.v1");
    public byte[] Protect(byte[] data)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("إعدادات الاتصال المحمية تتطلب Windows.");
        return ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    }
    public byte[] Unprotect(byte[] data)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("إعدادات الاتصال المحمية تتطلب Windows.");
        return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
    }
}

/// <summary>Per-account encrypted connection configuration, outside installation and update files.</summary>
public class ConnectionSettingsStore
{
    private readonly string _path;
    private readonly ISetupSecretProtector _protector;
    public ConnectionSettingsStore(string path, ISetupSecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public string? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            if (new FileInfo(_path).Length > 64 * 1024) throw new InvalidOperationException();
            var bytes = _protector.Unprotect(File.ReadAllBytes(_path));
            var settings = JsonSerializer.Deserialize<Settings>(bytes);
            if (settings is null || settings.Version != 1) throw new InvalidOperationException();
            return SetupDatabaseService.ValidateConnectionString(settings.ConnectionString).ConnectionString;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException("تعذر قراءة إعدادات الاتصال المحفوظة لهذا المستخدم. أعد ضبط الاتصال من معالج الإعداد؛ لم تتغير قاعدة البيانات.");
        }
    }

    public void Save(string connectionString)
    {
        var normalized = SetupDatabaseService.ValidateConnectionString(connectionString).ConnectionString;
        // Encrypt before creating the temporary file; a protection/write failure
        // never replaces the previous working connection.
        var plain = JsonSerializer.SerializeToUtf8Bytes(new Settings(1, normalized));
        byte[] protectedData;
        try { protectedData = _protector.Protect(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "connection-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, protectedData);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record Settings(int Version, string ConnectionString);
}
