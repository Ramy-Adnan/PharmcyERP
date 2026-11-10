using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace PharmacyERP.Application.Features.Licensing;

/// <summary>The signed contract shared by the customer verifier and the private vendor tool.</summary>
public sealed class LicensePayload
{
    public string LicenseKey { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int MaxBranches { get; set; }
    public int MaxUsers { get; set; }
    public string HardwareId { get; set; } = string.Empty;
}

public sealed class SignedLicenseDocument
{
    public int SchemaVersion { get; set; } = LicenseDocumentCodec.SchemaVersion;
    public string Product { get; set; } = LicenseDocumentCodec.Product;
    public string Algorithm { get; set; } = LicenseDocumentCodec.Algorithm;
    public LicensePayload? Payload { get; set; }
    public string Signature { get; set; } = string.Empty;
}

public static class LicenseDocumentCodec
{
    public const int SchemaVersion = 2;
    public const string Product = "PharmacyERP";
    public const string Algorithm = "RSA-SHA256-PSS";
    public const int MaxDocumentCharacters = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Sign(LicensePayload payload, RSA privateKey)
    {
        var validation = ValidatePayload(payload);
        if (validation is not null) throw new ArgumentException(validation, nameof(payload));
        ValidateKeySize(privateKey);
        var signature = privateKey.SignData(GetSigningBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return JsonSerializer.Serialize(new SignedLicenseDocument
        {
            Payload = payload,
            Signature = Convert.ToBase64String(signature)
        }, JsonOptions);
    }

    public static bool TryRead(string? content, out SignedLicenseDocument? document, out string error)
    {
        document = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxDocumentCharacters)
        {
            error = "ملف الترخيص فارغ أو يتجاوز الحجم المسموح.";
            return false;
        }
        try
        {
            // Inspect the explicit version before deserializing. Old HMAC documents must
            // never become accepted through a model's default property values.
            using var json = JsonDocument.Parse(content);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "ملف الترخيص غير صحيح.";
                return false;
            }
            if (!json.RootElement.TryGetProperty(nameof(SignedLicenseDocument.SchemaVersion), out var version))
            {
                error = "ملف الترخيص من الإصدار القديم. اطلب من المورّد إعادة إصداره للنسخة الجديدة.";
                return false;
            }
            if (!version.TryGetInt32(out var number) || number != SchemaVersion)
            {
                error = "إصدار ملف الترخيص غير مدعوم. اطلب ملفاً متوافقاً من المورّد.";
                return false;
            }
            document = JsonSerializer.Deserialize<SignedLicenseDocument>(content);
            if (document is null || document.Product != Product || document.Algorithm != Algorithm
                || document.Payload is null || string.IsNullOrWhiteSpace(document.Signature))
            {
                error = "ملف الترخيص غير مكتمل أو صادر لمنتج آخر.";
                return false;
            }
            error = ValidatePayload(document.Payload) ?? string.Empty;
            return error.Length == 0;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            document = null;
            error = "تعذّرت قراءة ملف الترخيص؛ الملف تالف أو غير صحيح.";
            return false;
        }
    }

    public static bool Verify(SignedLicenseDocument document, RSA publicKey)
    {
        if (document.SchemaVersion != SchemaVersion || document.Product != Product
            || document.Algorithm != Algorithm || document.Payload is null
            || ValidatePayload(document.Payload) is not null) return false;
        try
        {
            ValidateKeySize(publicKey);
            var signature = Convert.FromBase64String(document.Signature);
            return publicKey.VerifyData(GetSigningBytes(document.Payload), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    public static RSA ReadPublicKey(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem) || pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            throw new ArgumentException("يجب اختيار المفتاح العام فقط؛ لا تضمّن المفتاح الخاص في البرنامج.");
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            ValidateKeySize(rsa);
            return rsa;
        }
        catch { rsa.Dispose(); throw; }
    }

    public static string? ValidatePayload(LicensePayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.LicenseKey) || payload.LicenseKey.Length > 64
            || payload.LicenseKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            return "معرّف الترخيص غير صالح.";
        if (!ValidText(payload.CustomerName, 160) || !ValidText(payload.HardwareId, 256))
            return "اسم العميل أو معرّف الجهاز غير صالح.";
        if (payload.MaxBranches is < 1 or > 10000 || payload.MaxUsers is < 1 or > 100000)
            return "حدود المستخدمين والفروع غير صالحة.";
        if (payload.IssuedAtUtc.Kind != DateTimeKind.Utc || payload.IssuedAtUtc.Year < 2020)
            return "تاريخ إصدار الترخيص غير صالح؛ يلزم وقت UTC.";
        if (payload.ExpiryDate.HasValue && payload.ExpiryDate.Value.Date < payload.IssuedAtUtc.Date)
            return "تاريخ انتهاء الترخيص يسبق تاريخ إصداره.";
        return null;
    }

    public static LicenseInfoDto ToInfo(LicensePayload payload) => new()
    {
        LicenseKey = payload.LicenseKey,
        CustomerName = payload.CustomerName,
        IssuedAtUtc = payload.IssuedAtUtc,
        ExpiryDate = payload.ExpiryDate,
        MaxBranches = payload.MaxBranches,
        MaxUsers = payload.MaxUsers,
        HardwareId = payload.HardwareId
    };

    private static bool ValidText(string? value, int max) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= max && !value.Any(char.IsControl);

    private static void ValidateKeySize(RSA key)
    {
        if (key.KeySize < 2048) throw new ArgumentException("مفتاح RSA يجب ألا يقل عن 2048 بت.");
    }

    private static byte[] GetSigningBytes(LicensePayload payload)
    {
        // Explicit order, names and UTC/date formats make the signature independent of
        // JSON whitespace, machine culture and serializer property ordering.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber(nameof(SignedLicenseDocument.SchemaVersion), SchemaVersion);
            writer.WriteString(nameof(SignedLicenseDocument.Product), Product);
            writer.WriteString(nameof(SignedLicenseDocument.Algorithm), Algorithm);
            writer.WriteStartObject(nameof(SignedLicenseDocument.Payload));
            writer.WriteString(nameof(LicensePayload.LicenseKey), payload.LicenseKey);
            writer.WriteString(nameof(LicensePayload.CustomerName), payload.CustomerName);
            writer.WriteString(nameof(LicensePayload.IssuedAtUtc), payload.IssuedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            if (payload.ExpiryDate.HasValue)
                writer.WriteString(nameof(LicensePayload.ExpiryDate), payload.ExpiryDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            else writer.WriteNull(nameof(LicensePayload.ExpiryDate));
            writer.WriteNumber(nameof(LicensePayload.MaxBranches), payload.MaxBranches);
            writer.WriteNumber(nameof(LicensePayload.MaxUsers), payload.MaxUsers);
            writer.WriteString(nameof(LicensePayload.HardwareId), payload.HardwareId);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
