using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>A grouping for Expenses (e.g. "Rent", "Utilities", "Salaries"), each defaulting to a specific Expense account for posting.</summary>
public class ExpenseCategory : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public int DefaultExpenseAccountId { get; set; }
    public ChartOfAccount DefaultExpenseAccount { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}
