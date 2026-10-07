namespace PharmacyERP.Application.Features.Reports.DTOs;

/// <summary>Live KPI snapshot shown on the main dashboard, always scoped to "today" and optionally to one branch.</summary>
public class DashboardSummaryDto
{
    public DateTime AsOfDate { get; set; }

    public decimal TodaySalesTotal { get; set; }
    public decimal TodayCreditSalesTotal { get; set; }
    public decimal TodayCashSalesTotal { get; set; }
    public decimal TodayCardSalesTotal { get; set; }
    public decimal TodayCreditDeposits { get; set; }
    public decimal TodayDebtCollections { get; set; }
    public decimal TodayRevenueTotal => TodaySalesTotal + TodayCreditSalesTotal;
    public decimal TodayCashMovement { get; set; }
    public int TodayInvoiceCount { get; set; }
    public decimal TodayCogs { get; set; }
    public decimal TodayGrossProfit { get; set; }

    public decimal TodayExpensesTotal { get; set; }
    public decimal TodayNetCashPosition => TodayCashMovement;

    public int LowStockItemCount { get; set; }
    public int ExpiringSoonCount { get; set; }
    public int ExpiredCount { get; set; }

    public List<TopSellingItemDto> TopSellingItemsToday { get; set; } = new();
}

public class TopSellingItemDto
{
    public string ItemName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal Revenue { get; set; }
}
