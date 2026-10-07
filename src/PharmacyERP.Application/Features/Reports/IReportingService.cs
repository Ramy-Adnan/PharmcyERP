using PharmacyERP.Application.Features.Reports.DTOs;

namespace PharmacyERP.Application.Features.Reports;

/// <summary>
/// Read-only aggregation surface over data owned by every other module
/// (Sales, Inventory, Accounting). Nothing here writes any data — every
/// method is a query that reshapes existing records for a dashboard tile or
/// a printable/exportable report. Kept as its own module rather than bolted
/// onto Sales/Inventory/Accounting individually, since several reports
/// (Income Statement, the main dashboard) necessarily cut across all three.
/// </summary>
public interface IReportingService
{
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(int? branchId, CancellationToken cancellationToken = default);

    Task<TrialBalanceDto> GetTrialBalanceAsync(DateTime asOfDate, int? branchId, CancellationToken cancellationToken = default);

    Task<IncomeStatementDto> GetIncomeStatementAsync(DateTime fromDate, DateTime toDate, int? branchId, CancellationToken cancellationToken = default);

    Task<List<InventoryMovementLineDto>> GetInventoryMovementAsync(
        DateTime fromDate, DateTime toDate, int? branchId, int? itemId, CancellationToken cancellationToken = default);

    Task<SalesSummaryDto> GetSalesSummaryAsync(DateTime fromDate, DateTime toDate, int? branchId, CancellationToken cancellationToken = default);
}
