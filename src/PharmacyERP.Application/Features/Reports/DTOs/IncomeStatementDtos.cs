namespace PharmacyERP.Application.Features.Reports.DTOs;

public class ExpenseCategoryAmountDto
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>
/// A simplified income statement for a date range: Revenue less Cost of
/// Goods Sold (computed from the exact per-batch cost snapshotted on each
/// sale at the time it happened, not current purchase prices) gives Gross
/// Profit; less recorded Expenses gives Net Profit.
/// </summary>
public class IncomeStatementDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    public decimal TotalRevenue { get; set; }
    public decimal TotalCogs { get; set; }
    public decimal GrossProfit => TotalRevenue - TotalCogs;

    public List<ExpenseCategoryAmountDto> ExpensesByCategory { get; set; } = new();
    public decimal TotalExpenses => ExpensesByCategory.Sum(e => e.Amount);

    public decimal NetProfit => GrossProfit - TotalExpenses;
}
