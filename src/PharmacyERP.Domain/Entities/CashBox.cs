using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>A physical cash drawer/register at a Branch, backed by a specific Asset account in the Chart of Accounts.</summary>
public class CashBox : AuditableEntity
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public int AccountId { get; set; }
    public ChartOfAccount Account { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}
