using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>A recorded outgoing business expense, always posted to its own JournalEntry (Dr Expense account / Cr Cash or Bank).</summary>
public class Expense : AuditableEntity
{
    public string Number { get; set; } = string.Empty;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public int ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;

    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string? Description { get; set; }

    public CashSourceType SourceType { get; set; }
    public int? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }
    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public int JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;
}
