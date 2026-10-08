using PharmacyERP.Domain.Common;
namespace PharmacyERP.Domain.Entities;
public class PurchaseImageImport : BaseEntity
{
    public Guid RequestId { get; set; }
    public int SupplierId { get; set; }
    public int GoodsReceiptNoteId { get; set; }
    public string SupplierInvoiceKey { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public decimal? ParsedTotal { get; set; }
    public decimal ReviewedTotal { get; set; }
    public DateTime ImportedAtUtc { get; set; }
    public int? ImportedByUserId { get; set; }
}
