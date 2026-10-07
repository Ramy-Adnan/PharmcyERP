using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// The supplier's bill for goods already received (or being billed directly).
/// AmountPaid is tracked directly on the invoice for now; a full Supplier
/// Payment ledger tied to cash/bank accounts is introduced in the Accounting
/// phase, at which point payments recorded here will roll up into it.
/// </summary>
public class PurchaseInvoice : AuditableEntity
{
    public string Number { get; set; } = string.Empty;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int? GoodsReceiptNoteId { get; set; }
    public GoodsReceiptNote? GoodsReceiptNote { get; set; }

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }

    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }

    public PurchaseInvoiceStatus Status { get; set; } = PurchaseInvoiceStatus.Unpaid;
    public string? Notes { get; set; }

    public ICollection<PurchaseInvoiceItem> Items { get; set; } = new List<PurchaseInvoiceItem>();

    public decimal AmountDue => Math.Max(0, TotalAmount - AmountPaid);
}
