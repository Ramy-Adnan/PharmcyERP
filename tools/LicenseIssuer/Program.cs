using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Internal vendor tool: do not distribute with the application.
// Supply the same PHARMACYERP_LICENSE_SIGNING_SECRET securely to both processes.
var SigningSecret = Environment.GetEnvironmentVariable("PHARMACYERP_LICENSE_SIGNING_SECRET")
    ?? throw new InvalidOperationException("Set PHARMACYERP_LICENSE_SIGNING_SECRET before issuing licenses.");

Console.WriteLine("=== أداة إصدار ترخيص Pharmacy ERP (للاستخدام الداخلي فقط) ===");
Console.Write("اسم العميل: ");
var customerName = Console.ReadLine() ?? "";

Console.Write("بصمة الجهاز (Hardware Fingerprint) المرسلة من العميل: ");
var hardwareId = Console.ReadLine() ?? "";

Console.Write("عدد الفروع المسموح بها: ");
var maxBranches = int.Parse(Console.ReadLine() ?? "1");

Console.Write("عدد المستخدمين المسموح بهم: ");
var maxUsers = int.Parse(Console.ReadLine() ?? "5");

Console.Write("مدة الترخيص بالأيام (اتركها فارغة لترخيص دائم): ");
var durationInput = Console.ReadLine();
DateTime? expiryDate = string.IsNullOrWhiteSpace(durationInput)
    ? null
    : DateTime.UtcNow.Date.AddDays(int.Parse(durationInput));

var payload = new LicensePayload
{
    LicenseKey = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
    CustomerName = customerName,
    IssuedAtUtc = DateTime.UtcNow,
    ExpiryDate = expiryDate,
    MaxBranches = maxBranches,
    MaxUsers = maxUsers,
    HardwareId = hardwareId
};

var canonical = JsonSerializer.Serialize(payload);
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningSecret));
var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));

var signedFile = new SignedLicenseFile { Payload = payload, Signature = signature };
var outputJson = JsonSerializer.Serialize(signedFile, new JsonSerializerOptions { WriteIndented = true });

var fileName = $"{customerName.Replace(' ', '-')}-{payload.LicenseKey}.lic";
File.WriteAllText(fileName, outputJson);

Console.WriteLine();
Console.WriteLine($"تم إنشاء ملف الترخيص: {fileName}");
Console.WriteLine("أرسل هذا الملف للعميل ليستورده من داخل شاشة (الترخيص) في التطبيق.");

class LicensePayload
{
    public string LicenseKey { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int MaxBranches { get; set; }
    public int MaxUsers { get; set; }
    public string HardwareId { get; set; } = string.Empty;
}

class SignedLicenseFile
{
    public LicensePayload Payload { get; set; } = new();
    public string Signature { get; set; } = string.Empty;
}
