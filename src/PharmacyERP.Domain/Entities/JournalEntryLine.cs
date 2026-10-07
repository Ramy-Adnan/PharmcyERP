using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>One debit or credit line of a JournalEntry. Exactly one of DebitAmount/CreditAmount is non-zero.</summary>
public class JournalEntryLine : AuditableEntity
{
    public int JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;

    public int AccountId { get; set; }
    public ChartOfAccount Account { get; set; } = null!;

    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? Description { get; set; }
}
