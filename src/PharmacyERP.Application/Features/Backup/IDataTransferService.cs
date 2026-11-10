using PharmacyERP.Application.Common.Models;

namespace PharmacyERP.Application.Features.Backup;

/// <summary>Moves the database and the current Windows user's pharmacy branding/photos together.</summary>
public interface IDataTransferService
{
    Task<Result<BackupFileDto>> ExportBundleAsync(string destinationZip, CancellationToken cancellationToken = default);
    Task<Result<DataTransferPreviewDto>> InspectBundleAsync(string bundlePath, CancellationToken cancellationToken = default);
    /// <summary>Requires explicit UI confirmation, exclusive application access and restart on success.</summary>
    Task<Result> ImportBundleAsync(string bundlePath, CancellationToken cancellationToken = default);
}

public sealed class DataTransferPreviewDto
{
    public string DatabaseName { get; init; } = string.Empty;
    public string? PharmacyName { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int ProductImageCount { get; init; }
    public bool HasReceiptSettings { get; init; }
}
