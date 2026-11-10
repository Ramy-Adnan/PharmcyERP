using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Licensing;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>
/// Offline verification using an embedded public key. No signing secret is
/// installed at the pharmacy. The local clock checkpoint detects ordinary clock
/// rollback; it is not a substitute for a trusted online clock or tamper-proof storage.
/// </summary>
public class LicenseService : ILicenseService
{
    public const string PublicKeyResourceName = "PharmacyERP.Licensing.PublicKey.pem";
    private readonly string _licenseFilePath;
    private readonly string _machineIdFilePath;
    private readonly string _clockFilePath;
    private readonly Func<string?> _publicKeyReader;
    private readonly Func<DateTime> _utcNow;
    private readonly string _machineName;
    private readonly object _sync = new();

    public LicenseService(IConfiguration configuration)
        : this(configuration, ReadEmbeddedPublicKey, () => DateTime.UtcNow, Environment.MachineName) { }

    /// <summary>Explicit dependencies allow crypto, expiry and clock checks without Windows or a database.</summary>
    public LicenseService(IConfiguration configuration, Func<string?> publicKeyReader,
        Func<DateTime>? utcNow = null, string? machineName = null)
    {
        var configuredFolder = configuration["ApplicationSettings:LicenseFolder"];
        var folder = string.IsNullOrWhiteSpace(configuredFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PharmacyERP")
            : configuredFolder;
        _licenseFilePath = Path.Combine(folder, "license.lic");
        _machineIdFilePath = Path.Combine(folder, "machine.id");
        _clockFilePath = Path.Combine(folder, "license-clock.date");
        _publicKeyReader = publicKeyReader ?? throw new ArgumentNullException(nameof(publicKeyReader));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _machineName = machineName ?? Environment.MachineName;
    }

    public string GetHardwareFingerprint()
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_machineIdFilePath)!);
            if (!File.Exists(_machineIdFilePath))
            {
                var temporary = _machineIdFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, Guid.NewGuid().ToString("N"));
                    File.Move(temporary, _machineIdFilePath, overwrite: false);
                }
                catch (IOException) when (File.Exists(_machineIdFilePath)) { }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            var machineGuid = File.ReadAllText(_machineIdFilePath).Trim();
            if (!Guid.TryParseExact(machineGuid, "N", out _))
                throw new InvalidOperationException("ملف معرّف الجهاز تالف. تواصل مع الدعم لاستعادة التفعيل؛ لا تحذف بيانات الصيدلية.");
            return $"{_machineName}-{machineGuid}";
        }
    }

    public Task<LicenseStatusDto> GetLicenseStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!File.Exists(_licenseFilePath))
            {
                return Task.FromResult(new LicenseStatusDto
                {
                    IsVerificationConfigured = HasPublicKey(),
                    Message = "لا يوجد ترخيص مُفعّل. تتاح قراءة البيانات والنسخ الاحتياطي؛ استورد ترخيصاً لفتح العمليات."
                });
            }
            try
            {
                if (new FileInfo(_licenseFilePath).Length > LicenseDocumentCodec.MaxDocumentCharacters * 4)
                    return Task.FromResult(Invalid("ملف الترخيص يتجاوز الحجم المسموح."));
                return Task.FromResult(ValidateLicenseContent(File.ReadAllText(_licenseFilePath), updateCheckpoint: true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return Task.FromResult(Invalid("تعذّر التحقق من الترخيص. تأكد من صلاحيات مجلد بيانات البرنامج وتواصل مع الدعم."));
            }
        }
    }

    public Task<Result<LicenseInfoDto>> ActivateLicenseAsync(string licenseFileContent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            try
            {
                var status = ValidateLicenseContent(licenseFileContent, updateCheckpoint: false);
                if (!status.IsValid || status.License is null)
                    return Task.FromResult(Result<LicenseInfoDto>.Failure(status.Message ?? "ملف الترخيص غير صالح."));

                Directory.CreateDirectory(Path.GetDirectoryName(_licenseFilePath)!);
                // Preserve the previous valid activation until writing the replacement succeeds.
                WriteAtomic(_licenseFilePath, licenseFileContent);
                SaveClockCheckpoint(_utcNow().ToUniversalTime().Date);
                return Task.FromResult(Result<LicenseInfoDto>.Success(status.License));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return Task.FromResult(Result<LicenseInfoDto>.Failure("تعذّر حفظ الترخيص. تأكد من صلاحيات مجلد بيانات البرنامج وحاول مجدداً."));
            }
        }
    }

    public async Task<bool> CanAddBranchAsync(int currentActiveBranchCount, CancellationToken cancellationToken = default)
    {
        var status = await GetLicenseStatusAsync(cancellationToken);
        return status.IsValid && status.License is not null && currentActiveBranchCount < status.License.MaxBranches;
    }

    public async Task<bool> CanAddUserAsync(int currentActiveUserCount, CancellationToken cancellationToken = default)
    {
        var status = await GetLicenseStatusAsync(cancellationToken);
        return status.IsValid && status.License is not null && currentActiveUserCount < status.License.MaxUsers;
    }

    private LicenseStatusDto ValidateLicenseContent(string content, bool updateCheckpoint)
    {
        if (!LicenseDocumentCodec.TryRead(content, out var document, out var error)) return Invalid(error);
        string? key;
        try { key = _publicKeyReader(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { key = null; }
        if (string.IsNullOrWhiteSpace(key))
            return Invalid("هذه النسخة لا تحتوي مفتاح تحقق للترخيص. اطلب من المورّد نسخة النشر التجارية.", configured: false);
        try
        {
            using var rsa = LicenseDocumentCodec.ReadPublicKey(key);
            if (!LicenseDocumentCodec.Verify(document!, rsa)) return Invalid("توقيع الترخيص غير صحيح؛ الملف معدّل أو صادر عن جهة أخرى.");
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return Invalid("مفتاح التحقق في نسخة البرنامج غير صالح. تواصل مع المورّد.", configured: false);
        }

        var payload = document!.Payload!;
        var info = LicenseDocumentCodec.ToInfo(payload);
        var today = _utcNow().ToUniversalTime().Date;
        if (!string.Equals(payload.HardwareId, GetHardwareFingerprint(), StringComparison.Ordinal))
            return new LicenseStatusDto
            {
                IsPresent = true, IsVerificationConfigured = true, IsHardwareMismatch = true, License = info,
                Message = "هذا الترخيص صادر لجهاز آخر. اطلب ترخيصاً مرتبطاً بمعرّف الجهاز الحالي."
            };
        if ((payload.IssuedAtUtc.Date - today).TotalDays > 1 || HasClockRollback(today))
            return new LicenseStatusDto
            {
                IsPresent = true, IsVerificationConfigured = true, IsClockRollbackDetected = true, License = info,
                Message = "تاريخ الجهاز يسبق تاريخ التحقق أو إصدار الترخيص. صحّح تاريخ Windows ثم أعد المحاولة؛ تتاح القراءة والنسخ الاحتياطي."
            };
        if (payload.ExpiryDate.HasValue && payload.ExpiryDate.Value.Date < today)
            return new LicenseStatusDto
            {
                IsPresent = true, IsVerificationConfigured = true, IsExpired = true, License = info,
                Message = $"انتهى الترخيص بتاريخ {payload.ExpiryDate.Value:yyyy-MM-dd}. تتاح قراءة البيانات والنسخ الاحتياطي؛ استورد ملف التجديد لاستئناف العمليات."
            };
        if (updateCheckpoint) SaveClockCheckpoint(today);
        var remaining = payload.ExpiryDate.HasValue ? (int?)(payload.ExpiryDate.Value.Date - today).TotalDays : null;
        return new LicenseStatusDto
        {
            IsPresent = true, IsVerificationConfigured = true, IsValid = true, License = info, DaysUntilExpiry = remaining,
            Message = remaining.HasValue && remaining <= 14 ? $"الترخيص صالح وينتهي خلال {remaining.Value} يوماً." : "الترخيص صالح."
        };
    }

    private bool HasPublicKey()
    {
        try
        {
            var pem = _publicKeyReader();
            if (string.IsNullOrWhiteSpace(pem)) return false;
            using var rsa = LicenseDocumentCodec.ReadPublicKey(pem);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException or ArgumentException) { return false; }
    }

    private bool HasClockRollback(DateTime today)
    {
        if (!File.Exists(_clockFilePath)) return false;
        var value = File.ReadAllText(_clockFilePath).Trim();
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var highest) || highest.Year < 2020)
            throw new InvalidOperationException("ملف التحقق الزمني غير صالح.");
        // A one-day tolerance avoids locking a pharmacy after ordinary time-zone/date corrections.
        return (highest.Date - today).TotalDays > 1;
    }

    private void SaveClockCheckpoint(DateTime today)
    {
        if (File.Exists(_clockFilePath))
        {
            var value = File.ReadAllText(_clockFilePath).Trim();
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var highest)
                && highest.Date >= today) return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_clockFilePath)!);
        WriteAtomic(_clockFilePath, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private LicenseStatusDto Invalid(string message, bool configured = true) => new()
    {
        IsPresent = true, IsVerificationConfigured = configured && HasPublicKey(), Message = message
    };

    private static string? ReadEmbeddedPublicKey()
    {
        using var stream = typeof(LicenseService).Assembly.GetManifestResourceStream(PublicKeyResourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
