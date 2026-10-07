using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Accounting.DTOs;

public class ExpenseCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DefaultExpenseAccountName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class ExpenseCategoryUpsertDto
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DefaultExpenseAccountId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ExpenseDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public DateTime ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public CashSourceType SourceType { get; set; }
}

public class ExpenseCreateDto
{
    public int BranchId { get; set; }
    public int ExpenseCategoryId { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public CashSourceType SourceType { get; set; } = CashSourceType.Cash;
    public int? CashBoxId { get; set; }
    public int? BankAccountId { get; set; }
}
