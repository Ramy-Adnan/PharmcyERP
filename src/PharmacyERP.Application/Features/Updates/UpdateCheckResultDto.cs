namespace PharmacyERP.Application.Features.Updates;

/// <summary>Result of comparing the running app's version against the version feed published on api.alliba.uk.</summary>
public class UpdateCheckResultDto
{
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string? LatestVersion { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ReleaseNotes { get; set; }

    /// <summary>When true, the update must be installed before the app can continue being used (e.g. a breaking database migration shipped).</summary>
    public bool IsMandatory { get; set; }
}
