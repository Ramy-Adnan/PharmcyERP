using PharmacyERP.Application.Common.Models;

namespace PharmacyERP.Application.Features.Licensing;

/// <summary>
/// Offline activation of an RSA-signed license bound to this installation.
/// Only the public verification key is embedded in the application; the vendor
/// keeps the private signing key and its issuing tool outside the customer build.
/// Offline operation cannot guarantee immediate revocation or prevent a modified
/// executable from bypassing checks.
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
