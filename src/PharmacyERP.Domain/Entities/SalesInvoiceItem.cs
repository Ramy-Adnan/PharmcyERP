using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// One sold line (an Item at a given price/quantity) on a SalesInvoice. The
/// specific Batch(es) that quantity was physically taken from are recorded
/// separately in BatchAllocations — a single sold line can straddle more
/// than one batch when FEFO exhausts the soonest-expiring one mid-line.
/// QuantityReturned accumulates as SalesReturns are processed against this
/// line, so the system always knows how much of it is still returnable.
/// </summary>
public class SalesInvoiceItem : BaseEntity
{
    public int SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    public int? ItemSaleUnitId { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int UnitsPerSale { get; set; } = 1;
    public string UnitName { get; set; } = string.Empty;
    public string? ItemDisplayName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }

    public int QuantityReturned { get; set; }

    public ICollection<SalesInvoiceItemBatch> BatchAllocations { get; set; } = new List<SalesInvoiceItemBatch>();
    public ICollection<SalesReturnItem> ReturnItems { get; set; } = new List<SalesReturnItem>();

    public int QuantityReturnable => Quantity - QuantityReturned;
}
