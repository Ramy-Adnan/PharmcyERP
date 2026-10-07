using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>A bank account the pharmacy holds, backed by a specific Asset account in the Chart of Accounts.</summary>
public class BankAccount : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;

    public int AccountId { get; set; }
    public ChartOfAccount Account { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}
