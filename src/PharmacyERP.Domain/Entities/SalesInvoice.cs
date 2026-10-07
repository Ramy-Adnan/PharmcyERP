using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A completed point-of-sale transaction. Stock is deducted line-by-line at
/// creation time via FEFO batch allocation (see SalesInvoiceItemBatch) — by
/// the time a SalesInvoice row exists, the corresponding StockTransactions
/// have already been written and are authoritative.
/// </summary>
public class SalesInvoice : AuditableEntity
{
    public string Number { get; set; } = string.Empty;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Set when one or more lines in this sale were dispensed against a prescription (see Prescription.SalesInvoices).</summary>
    public int? PrescriptionId { get; set; }
    public Prescription? Prescription { get; set; }

    public int CashierUserId { get; set; }
    public User CashierUser { get; set; } = null!;

    public DateTime SaleAtUtc { get; set; } = DateTime.UtcNow;

    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public decimal AmountTendered { get; set; }
    public decimal ChangeGiven { get; set; }

    public SalesInvoiceStatus Status { get; set; } = SalesInvoiceStatus.Completed;
    public string? Notes { get; set; }

    public ICollection<SalesInvoiceItem> Items { get; set; } = new List<SalesInvoiceItem>();
    public ICollection<SalesReturn> Returns { get; set; } = new List<SalesReturn>();
}
