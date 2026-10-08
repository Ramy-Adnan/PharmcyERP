using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

public class PurchaseOrderItem : BaseEntity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal TaxRatePercent { get; set; }

    public int QuantityOutstanding => Math.Max(0, QuantityOrdered - QuantityReceived);
    public decimal LineTotal => Math.Round(UnitCost * QuantityOrdered * (1 + TaxRatePercent / 100m), 2);
}
