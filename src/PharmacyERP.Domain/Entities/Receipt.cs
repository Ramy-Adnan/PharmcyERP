using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// Money received into the business outside of a POS sale — most commonly an
/// insurance company paying out an approved claim, but also usable for any
/// other miscellaneous incoming payment. Always posted to its own JournalEntry
/// (Dr Cash/Bank / Cr the receivable or revenue account it settles).
/// </summary>
public class Receipt : AuditableEntity
{
    public string Number { get; set; } = string.Empty;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public DateTime ReceiptDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string PayerName { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public CashSourceType SourceType { get; set; }
    public int? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }
    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    /// <summary>e.g. "InsuranceClaim" — null for a manually recorded miscellaneous receipt.</summary>
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }

    public int JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;
}
