using System.Text.Json.Serialization;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Purchasing.Imports;

public sealed record InvoiceImageInput(string FileName, string MimeType, byte[] Content);
public interface IInvoiceImageReader
{
    Task<Result<InvoiceImageDocument>> ReadAsync(InvoiceImageInput image, CancellationToken cancellationToken = default);
}
public sealed class InvoiceImageDocument
{
    [JsonPropertyName("supplier_name")] public string? SupplierName { get; set; }
    [JsonPropertyName("invoice_number")] public string? InvoiceNumber { get; set; }
    [JsonPropertyName("invoice_date")] public DateTime? InvoiceDate { get; set; }
    [JsonPropertyName("invoice_total")] public decimal? InvoiceTotal { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("lines")] public List<InvoiceImageLine> Lines { get; set; } = new();
    [JsonIgnore] public string SourceHash { get; set; } = string.Empty;
    [JsonIgnore] public string SourceFileName { get; set; } = string.Empty;
}
public sealed class InvoiceImageLine
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("barcode")] public string? Barcode { get; set; }
    [JsonPropertyName("quantity")] public int? Quantity { get; set; }
    [JsonPropertyName("bonus_quantity")] public int? BonusQuantity { get; set; }
    [JsonPropertyName("unit_price")] public decimal? UnitPrice { get; set; }
    [JsonPropertyName("line_total")] public decimal? LineTotal { get; set; }
    [JsonPropertyName("batch_number")] public string? BatchNumber { get; set; }
    [JsonPropertyName("expiry_date")] public DateTime? ExpiryDate { get; set; }
    [JsonPropertyName("declared_unit_count")] public int? DeclaredUnitCount { get; set; }
    [JsonPropertyName("declared_unit_kind")] public string? DeclaredUnitKind { get; set; }
    [JsonPropertyName("strength")] public string? Strength { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}
public sealed record InvoiceItemCandidate(int ItemId, string Name, string BaseUnitName, int PackageCount, decimal Score);
public sealed class InvoiceItemMatch
{
    public int? ItemId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<InvoiceItemCandidate> Candidates { get; set; } = new();
}
public sealed class PurchaseImageImportLine
{
    public int? ExistingItemId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? Strength { get; set; }
    public int CategoryId { get; set; }
    public int BaseUnitOfMeasureId { get; set; }
    public string ReceiveUnitName { get; set; } = "علبة";
    public int BaseUnitsPerReceiveUnit { get; set; }
    public ItemForm Form { get; set; } = ItemForm.Other;
    public bool RequiresPrescription { get; set; }
    public bool IsControlledSubstance { get; set; }
    public int Quantity { get; set; }
    public int BonusQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? SalePrice { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public bool Reviewed { get; set; }
}
public sealed class PurchaseImageImportRequest
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int SupplierId { get; set; }
    public int BranchId { get; set; }
    public int WarehouseId { get; set; }
    public string SupplierInvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public decimal? ParsedTotal { get; set; }
    public decimal ReviewedTotal { get; set; }
    public PurchasePricingType PurchaseType { get; set; }
    public List<PurchaseImageImportLine> Lines { get; set; } = new();
}
public sealed record PurchaseImageImportResult(int GoodsReceiptId, bool AlreadyImported, string ReceiptNumber);
public interface IPurchaseImageImportService
{
    Task<List<InvoiceItemMatch>> MatchAsync(int supplierId, IReadOnlyList<InvoiceImageLine> lines, CancellationToken cancellationToken = default);
    Task<Result<PurchaseImageImportResult>> SaveReviewedAsync(PurchaseImageImportRequest request, CancellationToken cancellationToken = default);
}
