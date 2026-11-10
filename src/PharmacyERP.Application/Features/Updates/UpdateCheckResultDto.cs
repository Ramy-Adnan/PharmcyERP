namespace PharmacyERP.Application.Features.Updates;

/// <summary>Result of comparing the running app's version against the version feed published on api.alliba.uk.</summary>
public class UpdateCheckResultDto
{
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string? LatestVersion { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Sha256 { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Publisher release metadata; does not force installation or interrupt a sale.</summary>
    public bool IsMandatory { get; set; }
}
