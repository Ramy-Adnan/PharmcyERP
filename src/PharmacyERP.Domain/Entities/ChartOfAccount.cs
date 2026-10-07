using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A single account in the pharmacy's Chart of Accounts. Accounts can be
/// nested (e.g. "1100 Current Assets" -> "1110 Cash and Banks" -> "1111 Main
/// Cash Box") purely for presentation/grouping; every JournalEntryLine posts
/// against a specific leaf account. System accounts (Cash, Sales Revenue,
/// Accounts Payable, etc.) are seeded automatically and cannot be deleted,
/// since every automatic posting from Sales/Purchasing/Insurance depends on
/// their existence.
/// </summary>
public class ChartOfAccount : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }

    public int? ParentAccountId { get; set; }
    public ChartOfAccount? ParentAccount { get; set; }

    public bool IsSystemAccount { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ChartOfAccount> ChildAccounts { get; set; } = new List<ChartOfAccount>();
    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
}
