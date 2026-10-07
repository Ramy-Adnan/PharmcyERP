using PharmacyERP.Application.Common.Models;

namespace PharmacyERP.Application.Features.Licensing;

/// <summary>
/// Offline license activation: the vendor issues a signed .lic file (a small
/// JSON payload plus an HMAC-SHA256 signature computed with a secret embedded
/// in this application) bound to a hardware fingerprint of the machine it was
/// issued for. There is no license server this application calls out to —
/// activation is "receive the file from the vendor, import it here" — which
/// keeps the pharmacy fully operable even with no internet connection, at
/// the cost of the signature only being as strong as keeping the embedded
/// secret out of a decompiled build; this is a standard, disclosed trade-off
/// for offline desktop licensing and not a claim of tamper-proof security.
/// </summary>
public interface ILicenseService
{
    /// <summary>A fingerprint identifying this machine (not personally identifying — machine name + a persisted local GUID), sent to the vendor when requesting a license file.</summary>
    string GetHardwareFingerprint();

    Task<LicenseStatusDto> GetLicenseStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates the signature and hardware binding of the given .lic file content and, if valid, installs it as the active license for this machine.</summary>
    Task<Result<LicenseInfoDto>> ActivateLicenseAsync(string licenseFileContent, CancellationToken cancellationToken = default);

    /// <summary>Whether one more branch can be created under the current license's MaxBranches limit.</summary>
    Task<bool> CanAddBranchAsync(int currentActiveBranchCount, CancellationToken cancellationToken = default);

    /// <summary>Whether one more user can be created under the current license's MaxUsers limit.</summary>
    Task<bool> CanAddUserAsync(int currentActiveUserCount, CancellationToken cancellationToken = default);
}
