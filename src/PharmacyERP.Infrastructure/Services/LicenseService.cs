using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Licensing;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>
/// Offline license file activation. A license file is a JSON payload plus an
/// HMAC-SHA256 signature computed with a secret embedded in this build; the
/// vendor generates and signs the file out-of-band (a small internal tool,
/// not shipped with the product) using the matching secret, and the
/// pharmacy's administrator imports the resulting .lic file here. This
/// requires no license server and works fully offline, at the disclosed
/// cost that the embedded secret is only as protected as the compiled
/// binary itself — acceptable, standard practice for offline desktop
/// licensing, and explicitly not presented as tamper-proof.
/// </summary>
public class LicenseService : ILicenseService
{
    // In a real deployment this constant is unique per build/customer and never committed to a
    // public repository; it is left as a placeholder here since this is a reference implementation.
    private static string SigningSecret => Environment.GetEnvironmentVariable("PHARMACYERP_LICENSE_SIGNING_SECRET")
        ?? throw new InvalidOperationException("Set PHARMACYERP_LICENSE_SIGNING_SECRET to use signed licenses.");

    private readonly string _licenseFilePath;
    private readonly string _machineIdFilePath;

    public LicenseService(IConfiguration configuration)
    {
        var configuredFolder = configuration["ApplicationSettings:LicenseFolder"];
        var dataFolder = string.IsNullOrWhiteSpace(configuredFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PharmacyERP")
            : configuredFolder;

        Directory.CreateDirectory(dataFolder);
        _licenseFilePath = Path.Combine(dataFolder, "license.lic");
        _machineIdFilePath = Path.Combine(dataFolder, "machine.id");
    }

    public string GetHardwareFingerprint()
    {
        // A lightweight, non-personally-identifying fingerprint: the machine name plus a GUID
        // generated once and persisted locally (rather than querying WMI hardware identifiers,
        // which adds complexity and occasionally requires elevated permissions to read reliably).
        string machineGuid;
        if (File.Exists(_machineIdFilePath))
        {
            machineGuid = File.ReadAllText(_machineIdFilePath).Trim();
        }
        else
        {
            machineGuid = Guid.NewGuid().ToString("N");
            File.WriteAllText(_machineIdFilePath, machineGuid);
        }

        return $"{Environment.MachineName}-{machineGuid}";
    }

    public Task<LicenseStatusDto> GetLicenseStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_licenseFilePath))
        {
            return Task.FromResult(new LicenseStatusDto
            {
                IsPresent = false,
                IsValid = false,
                Message = "لا يوجد ترخيص مُفعَّل على هذا الجهاز."
            });
        }

        var content = File.ReadAllText(_licenseFilePath);
        var status = ValidateLicenseContent(content);
        return Task.FromResult(status);
    }

    public Task<Result<LicenseInfoDto>> ActivateLicenseAsync(string licenseFileContent, CancellationToken cancellationToken = default)
    {
        var status = ValidateLicenseContent(licenseFileContent);

        if (!status.IsValid || status.License is null)
            return Task.FromResult(Result<LicenseInfoDto>.Failure(status.Message ?? "ملف الترخيص غير صالح."));

        File.WriteAllText(_licenseFilePath, licenseFileContent);
        return Task.FromResult(Result<LicenseInfoDto>.Success(status.License));
    }

    public async Task<bool> CanAddBranchAsync(int currentActiveBranchCount, CancellationToken cancellationToken = default)
    {
        var status = await GetLicenseStatusAsync(cancellationToken);
        if (!status.IsValid || status.License is null) return false;

        return currentActiveBranchCount < status.License.MaxBranches;
    }

    public async Task<bool> CanAddUserAsync(int currentActiveUserCount, CancellationToken cancellationToken = default)
    {
        var status = await GetLicenseStatusAsync(cancellationToken);
        if (!status.IsValid || status.License is null) return false;

        return currentActiveUserCount < status.License.MaxUsers;
    }

    private LicenseStatusDto ValidateLicenseContent(string content)
    {
        SignedLicenseFile? signedFile;
        try
        {
            signedFile = JsonSerializer.Deserialize<SignedLicenseFile>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return new LicenseStatusDto { IsPresent = true, IsValid = false, Message = "تعذّرت قراءة ملف الترخيص — الملف تالف أو غير صحيح." };
        }

        if (signedFile is null || string.IsNullOrWhiteSpace(signedFile.Signature))
            return new LicenseStatusDto { IsPresent = true, IsValid = false, Message = "ملف الترخيص غير مكتمل." };

        var expectedSignature = ComputeSignature(signedFile.Payload);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedSignature), Encoding.UTF8.GetBytes(signedFile.Signature)))
        {
            return new LicenseStatusDto
            {
                IsPresent = true,
                IsValid = false,
                Message = "توقيع ملف الترخيص غير صحيح — الملف قد يكون معدَّلاً أو غير صادر عن الجهة المرخِّصة."
            };
        }

        var payload = signedFile.Payload;
        var license = new LicenseInfoDto
        {
            LicenseKey = payload.LicenseKey,
            CustomerName = payload.CustomerName,
            IssuedAtUtc = payload.IssuedAtUtc,
            ExpiryDate = payload.ExpiryDate,
            MaxBranches = payload.MaxBranches,
            MaxUsers = payload.MaxUsers,
            HardwareId = payload.HardwareId
        };

        var currentHardwareId = GetHardwareFingerprint();
        if (!string.Equals(payload.HardwareId, currentHardwareId, StringComparison.Ordinal))
        {
            return new LicenseStatusDto
            {
                IsPresent = true,
                IsValid = false,
                IsHardwareMismatch = true,
                License = license,
                Message = "هذا الترخيص صادر لجهاز آخر. الرجاء طلب ترخيص جديد مرتبط بهذا الجهاز من الجهة المرخِّصة."
            };
        }

        if (payload.ExpiryDate.HasValue && payload.ExpiryDate.Value.Date < DateTime.UtcNow.Date)
        {
            return new LicenseStatusDto
            {
                IsPresent = true,
                IsValid = false,
                IsExpired = true,
                License = license,
                Message = $"انتهت صلاحية الترخيص بتاريخ {payload.ExpiryDate.Value:yyyy-MM-dd}."
            };
        }

        int? daysUntilExpiry = payload.ExpiryDate.HasValue
            ? (int)(payload.ExpiryDate.Value.Date - DateTime.UtcNow.Date).TotalDays
            : null;

        return new LicenseStatusDto
        {
            IsPresent = true,
            IsValid = true,
            DaysUntilExpiry = daysUntilExpiry,
            License = license,
            Message = daysUntilExpiry.HasValue && daysUntilExpiry.Value <= 14
                ? $"الترخيص صالح، وينتهي خلال {daysUntilExpiry.Value} يوماً."
                : "الترخيص صالح."
        };
    }

    private static string ComputeSignature(LicensePayload payload)
    {
        var canonical = JsonSerializer.Serialize(payload);
        var keyBytes = Encoding.UTF8.GetBytes(SigningSecret);
        var messageBytes = Encoding.UTF8.GetBytes(canonical);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(messageBytes);
        return Convert.ToBase64String(hash);
    }

    private class LicensePayload
    {
        public string LicenseKey { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateTime IssuedAtUtc { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int MaxBranches { get; set; }
        public int MaxUsers { get; set; }
        public string HardwareId { get; set; } = string.Empty;
    }

    private class SignedLicenseFile
    {
        public LicensePayload Payload { get; set; } = new();
        public string Signature { get; set; } = string.Empty;
    }
}
