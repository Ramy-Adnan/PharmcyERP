using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

public class GoodsReceiptItem : BaseEntity
{
    public int GoodsReceiptNoteId { get; set; }
    public GoodsReceiptNote GoodsReceiptNote { get; set; } = null!;

    /// <summary>Optional link back to the ordered line, used to reconcile PurchaseOrderItem.QuantityReceived.</summary>
    public int? PurchaseOrderItemId { get; set; }
    public PurchaseOrderItem? PurchaseOrderItem { get; set; }

    public int? ItemSaleUnitId { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime ExpiryDate { get; set; }

    public int BonusQuantity { get; set; }
    public int QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal SalePrice { get; set; }
}
