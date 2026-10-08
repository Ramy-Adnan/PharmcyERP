using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// Records the physical arrival of stock at a branch's warehouse. Posting a
/// GoodsReceiptNote is the single point where Purchasing hands off to
/// Inventory: each line creates or tops up a Batch and writes a Receipt
/// StockTransaction, keeping both modules' ledgers consistent.
/// </summary>
public class GoodsReceiptNote : AuditableEntity
{
    public PurchasePricingType PurchaseType { get; set; }

    public string Number { get; set; } = string.Empty;

    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public DateTime ReceiptDate { get; set; }
    public GoodsReceiptStatus Status { get; set; } = GoodsReceiptStatus.Draft;
    public string? Notes { get; set; }

    public ICollection<GoodsReceiptItem> Items { get; set; } = new List<GoodsReceiptItem>();
}
