using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

public class PurchaseInvoiceItem : BaseEntity
{
    public int PurchaseInvoiceId { get; set; }
    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;

    public int? ItemSaleUnitId { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int BonusQuantity { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}
