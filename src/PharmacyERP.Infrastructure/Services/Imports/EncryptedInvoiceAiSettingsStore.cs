using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PharmacyERP.Application.Features.Purchasing.Imports;

namespace PharmacyERP.Infrastructure.Services.Imports;

public interface IInvoiceAiSecretProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public sealed class WindowsInvoiceAiSecretProtector : IInvoiceAiSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PharmacyERP.InvoiceAI.v1");
    public byte[] Protect(byte[] data)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("تشفير إعدادات التطبيق يتطلب Windows.");
        return ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    }
    public byte[] Unprotect(byte[] data)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("تشفير إعدادات التطبيق يتطلب Windows.");
        return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
    }
}

/// <summary>Per-installation configuration outside the checkout, encrypted using Windows DPAPI.</summary>
public sealed class EncryptedInvoiceAiSettingsStore : IInvoiceAiSettingsStore
{
    private readonly IInvoiceAiSecretProtector _protector;
    private readonly string _filePath;
    private readonly object _sync = new();
    public EncryptedInvoiceAiSettingsStore(IInvoiceAiSecretProtector protector, string? filePath = null)
    {
        _protector = protector;
        _filePath = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmacyERP", "invoice-ai-settings.dat");
    }
    public InvoiceAiSettings? Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_filePath)) return null;
            try
            {
                if (new FileInfo(_filePath).Length > 64 * 1024) throw new InvalidOperationException();
                var settings = JsonSerializer.Deserialize<InvoiceAiSettings>(_protector.Unprotect(File.ReadAllBytes(_filePath)))
                    ?? throw new InvalidOperationException();
                Validate(settings);
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException)
            { throw new InvalidOperationException("تعذر قراءة إعدادات خدمة الصور المحفوظة. افتح إعدادات النظام وأعد حفظ اختيار الخدمة."); }
        }
    }
    public void Save(InvoiceAiSettings settings)
    {
        Validate(settings);
        // Encrypt before replacing the file. Failed encryption or writing retains the previous key.
        var data = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(settings));
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath))!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $"invoice-ai-{Guid.NewGuid():N}.tmp");
            try { File.WriteAllBytes(temporary, data); File.Move(temporary, _filePath, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public static void Validate(InvoiceAiSettings settings)
    {
        if (settings.Provider is not ("LocalOCR" or "Gemini" or "OpenAI")) throw new InvalidOperationException("اختر OCR المحلي أو Gemini أو OpenAI.");
        if (settings.Provider == "LocalOCR")
        {
            if (settings.Model != "tesseract-ara-eng" || settings.ApiKey != string.Empty) throw new InvalidOperationException("OCR المحلي لا يحتاج مفتاح API ويستخدم العربية والإنجليزية المرفقتين.");
            return;
        }
        var pattern = settings.Provider == "Gemini" ? @"\Agemini-[a-zA-Z0-9._-]+\z" : @"\A[a-zA-Z0-9][a-zA-Z0-9._:-]*\z";
        if (string.IsNullOrWhiteSpace(settings.Model) || settings.Model.Length > 100 || !Regex.IsMatch(settings.Model, pattern))
            throw new InvalidOperationException("اسم النموذج غير صالح.");
        if (settings.ApiKey is null || settings.ApiKey.Length > 512 || settings.ApiKey.Any(char.IsControl))
            throw new InvalidOperationException("مفتاح الخدمة غير صالح؛ انسخه من جديد دون أسطر إضافية.");
    }
}
