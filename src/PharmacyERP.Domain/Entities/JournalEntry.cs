using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A double-entry journal entry: the sum of all its JournalEntryLines' debits
/// must equal the sum of their credits (enforced by AccountingService, not
/// the database). Entries created manually start as a draft the accountant
/// can review before posting; entries generated automatically from Sales,
/// Purchasing, or Insurance are posted immediately since the underlying
/// business transaction is already final by the time it reaches accounting.
/// </summary>
public class JournalEntry : AuditableEntity
{
    public string Number { get; set; } = string.Empty;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string Description { get; set; } = string.Empty;

    /// <summary>e.g. "SalesInvoice", "PurchaseInvoice", "InsuranceClaim" — null for manual entries.</summary>
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }

    public bool IsPosted { get; set; }
    public DateTime? PostedAtUtc { get; set; }

    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
}
