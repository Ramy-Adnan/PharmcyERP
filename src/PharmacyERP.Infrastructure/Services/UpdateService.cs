using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PharmacyERP.Application.Features.Updates;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>
/// Checks a small JSON version manifest hosted on api.alliba.uk (the same
/// server already hosting the update infrastructure for the company's other
/// desktop apps) and, when a newer version is published, downloads and
/// silently launches the Advanced Installer-built installer for it.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _updateFeedUrl;

    public UpdateService(IConfiguration configuration)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _updateFeedUrl = configuration["ApplicationSettings:UpdateFeedUrl"]
            ?? "https://api.alliba.uk/updates/pharmacy-erp/latest.json";
    }

    public async Task<UpdateCheckResultDto> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();
        var result = new UpdateCheckResultDto { CurrentVersion = currentVersion };

        try
        {
            using var response = await _httpClient.GetAsync(_updateFeedUrl, cancellationToken);
            if (!response.IsSuccessStatusCode) return result;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version)) return result;

            result.LatestVersion = manifest.Version;
            result.DownloadUrl = manifest.DownloadUrl;
            result.ReleaseNotes = manifest.ReleaseNotes;
            result.IsMandatory = manifest.Mandatory;

            if (Version.TryParse(manifest.Version, out var latest) && Version.TryParse(currentVersion, out var current))
                result.IsUpdateAvailable = latest > current;
        }
        catch
        {
            // Network failures, DNS issues, or an unreachable update server must never prevent
            // the pharmacy from opening the app — treat any error here as "no update available".
        }

        return result;
    }

    public async Task DownloadAndLaunchInstallerAsync(string downloadUrl, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"PharmacyERP-Update-{Guid.NewGuid():N}.exe");

        using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                if (totalBytes > 0 && progress is not null)
                    progress.Report((int)(totalRead * 100 / totalBytes));
            }
        }

        // Advanced Installer-built installers support a silent switch (/S or /qn depending on
        // the underlying engine); /S is used here as the company's existing installers are
        // built with the NSIS-compatible Advanced Installer silent-install profile.
        Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            Arguments = "/S",
            UseShellExecute = true
        });
    }

    private static string GetCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string? ReleaseNotes { get; set; }
        public bool Mandatory { get; set; }
    }
}
