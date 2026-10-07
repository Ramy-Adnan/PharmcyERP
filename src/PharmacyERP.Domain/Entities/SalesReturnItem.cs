using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

public class SalesReturnItem : BaseEntity
{
    public int SalesReturnId { get; set; }
    public SalesReturn SalesReturn { get; set; } = null!;

    public int SalesInvoiceItemId { get; set; }
    public SalesInvoiceItem SalesInvoiceItem { get; set; } = null!;

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
