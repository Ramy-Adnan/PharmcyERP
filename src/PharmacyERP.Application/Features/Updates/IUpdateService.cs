namespace PharmacyERP.Application.Features.Updates;

/// <summary>
/// Checks a publisher HTTPS manifest and verifies the downloaded installer
/// against its required SHA-256 before launching the interactive Windows setup.
/// </summary>
public interface IUpdateService
{
    /// <summary>Network/manifest failures are returned in ErrorMessage; cancellation is honored. Checking never blocks app startup.</summary>
    Task<UpdateCheckResultDto> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the installer referenced by <paramref name="downloadUrl"/> to a temp
    /// file, reporting progress via <paramref name="progress"/> (0-100), then verifies
    /// <paramref name="expectedSha256"/> and launches interactive setup. A missing hash
    /// is rejected. The caller is expected to shut the application down
    /// immediately afterward so the installer can overwrite the running executable.
    /// </summary>
    Task DownloadAndLaunchInstallerAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default, string? expectedSha256 = null);
}
