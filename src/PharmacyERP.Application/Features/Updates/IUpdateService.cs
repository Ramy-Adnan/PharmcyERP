namespace PharmacyERP.Application.Features.Updates;

/// <summary>
/// Silent auto-update client mirroring the update infrastructure already
/// running on api.alliba.uk for the company's other desktop applications:
/// the app checks a small JSON version manifest, and if a newer version is
/// published, downloads the installer and launches it, exiting the current
/// process so Advanced Installer's silent-install flow can replace the files.
/// </summary>
public interface IUpdateService
{
    /// <summary>Hits the version-check endpoint. Never throws — network failures are reported as "no update available" so a flaky connection never blocks the app from starting.</summary>
    Task<UpdateCheckResultDto> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the installer referenced by <paramref name="downloadUrl"/> to a temp
    /// file, reporting progress via <paramref name="progress"/> (0-100), then launches it
    /// silently and returns. The caller is expected to shut the application down
    /// immediately afterward so the installer can overwrite the running executable.
    /// </summary>
    Task DownloadAndLaunchInstallerAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
}
