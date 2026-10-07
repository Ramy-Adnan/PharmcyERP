using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// Money paid out of the business outside of the Purchasing module's own
/// supplier-payment recording — kept as a general-purpose outgoing payment
/// record (e.g. a manual payment not tied to any purchase invoice). Always
/// posted to its own JournalEntry (Dr the payable/expense it settles / Cr Cash or Bank).
/// </summary>
public class Payment : AuditableEntity
{
    public string Number { get; set; } = string.Empty;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string PayeeName { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public CashSourceType SourceType { get; set; }
    public int? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }
    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }

    public int JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;
}
