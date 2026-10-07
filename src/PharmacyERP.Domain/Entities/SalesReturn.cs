using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>A customer return processed against a specific SalesInvoice, restocking the original batches.</summary>
public class SalesReturn : AuditableEntity
{
    public string Number { get; set; } = string.Empty;

    public int SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public DateTime ReturnAtUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }

    public int ProcessedByUserId { get; set; }
    public User ProcessedByUser { get; set; } = null!;

    public ICollection<SalesReturnItem> Items { get; set; } = new List<SalesReturnItem>();
}
