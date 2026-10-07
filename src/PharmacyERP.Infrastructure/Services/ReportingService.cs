using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Reports;
using PharmacyERP.Application.Features.Reports.DTOs;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

public class ReportingService : IReportingService
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTime _dateTime;

    private const int DefaultExpiringSoonWindowDays = 30;

    public ReportingService(IApplicationDbContext context, IDateTime dateTime)
    {
        _context = context;
        _dateTime = dateTime;
    }

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(int? branchId, CancellationToken cancellationToken = default)
    {
        var today = _dateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var invoicesQuery = _context.SalesInvoices
            .Where(s => s.Status != SalesInvoiceStatus.Voided && s.SaleAtUtc >= today && s.SaleAtUtc < tomorrow);
        if (branchId.HasValue) invoicesQuery = invoicesQuery.Where(s => s.BranchId == branchId.Value);

        var todayInvoices = await invoicesQuery
            .Include(s => s.Items).ThenInclude(i => i.BatchAllocations)
            .Include(s => s.Items).ThenInclude(i => i.Item)
            .ToListAsync(cancellationToken);

        var returnedQuery = _context.SalesReturns.Where(r => r.ReturnAtUtc >= today && r.ReturnAtUtc < tomorrow);
        if (branchId.HasValue) returnedQuery = returnedQuery.Where(r => r.BranchId == branchId.Value);
        var returnedToday = await returnedQuery.Include(r => r.SalesInvoice).ToListAsync(cancellationToken);
        var todayCash = todayInvoices.Where(i => i.PaymentMethod == PaymentMethod.Cash).Sum(i => i.TotalAmount)
            - returnedToday.Where(r => r.SalesInvoice.PaymentMethod == PaymentMethod.Cash).Sum(r => r.TotalAmount);
        var todayCard = todayInvoices.Where(i => i.PaymentMethod == PaymentMethod.Card).Sum(i => i.TotalAmount)
            - returnedToday.Where(r => r.SalesInvoice.PaymentMethod == PaymentMethod.Card).Sum(r => r.TotalAmount);
        var todayCredit = todayInvoices.Where(i => i.PaymentMethod == PaymentMethod.Credit).Sum(i => i.TotalAmount)
            - returnedToday.Where(r => r.SalesInvoice.PaymentMethod == PaymentMethod.Credit).Sum(r => r.TotalAmount);
        var todaySalesTotal = todayCash + todayCard;
        var debtReceipts = _context.Receipts.Where(r => r.ReferenceType == "CustomerCredit" && r.ReceiptDate >= today && r.ReceiptDate < tomorrow);
        if (branchId.HasValue) debtReceipts = debtReceipts.Where(r => r.BranchId == branchId.Value);
        var received = await debtReceipts.AsNoTracking().ToListAsync(cancellationToken);
        var receiptInvoiceIds = received.Where(r => r.ReferenceId.HasValue).Select(r => r.ReferenceId!.Value).Distinct().ToList();
        var invoiceNumbers = await _context.SalesInvoices.Where(i => receiptInvoiceIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Number }).ToDictionaryAsync(i => i.Id, i => i.Number, cancellationToken);
        bool IsInitialPayment(PharmacyERP.Domain.Entities.Receipt r) => r.ReferenceId.HasValue
            && invoiceNumbers.TryGetValue(r.ReferenceId.Value, out var number) && number.StartsWith("SI-", StringComparison.Ordinal)
            && r.Number == "RCT-" + number[3..];
        var creditDeposits = received.Where(IsInitialPayment).Sum(r => r.Amount);
        var debtCollections = received.Where(r => !IsInitialPayment(r)).Sum(r => r.Amount);
        var cashLines = _context.JournalEntryLines.Where(l => l.Account.Code == "1110" && l.JournalEntry.IsPosted
            && l.JournalEntry.EntryDate >= today && l.JournalEntry.EntryDate < tomorrow);
        if (branchId.HasValue) cashLines = cashLines.Where(l => l.JournalEntry.BranchId == branchId.Value);
        var cashMovement = await cashLines.SumAsync(l => l.DebitAmount - l.CreditAmount, cancellationToken);
        var todayCogs = todayInvoices.Sum(s => s.Items.Sum(i => i.BatchAllocations.Sum(a => a.QuantityTaken * a.UnitCost)));

        var expensesQuery = _context.Expenses.Where(e => e.ExpenseDate >= today && e.ExpenseDate < tomorrow);
        if (branchId.HasValue) expensesQuery = expensesQuery.Where(e => e.BranchId == branchId.Value);
        var todayExpensesTotal = await expensesQuery.SumAsync(e => e.Amount, cancellationToken);

        var batchesQuery = _context.Batches.Include(b => b.Item).Include(b => b.Warehouse).AsQueryable();
        if (branchId.HasValue) batchesQuery = batchesQuery.Where(b => b.Warehouse.BranchId == branchId.Value);

        var batches = await batchesQuery.Where(b => b.QuantityOnHand > 0).ToListAsync(cancellationToken);

        var stockByItem = batches.GroupBy(b => b.ItemId).Select(g => new { ItemId = g.Key, Total = g.Sum(b => b.QuantityOnHand) }).ToList();
        var itemThresholds = await _context.Items
            .Where(i => stockByItem.Select(s => s.ItemId).Contains(i.Id))
            .Select(i => new { i.Id, i.MinStockLevel })
            .ToListAsync(cancellationToken);

        var lowStockCount = stockByItem.Count(s =>
            itemThresholds.Any(t => t.Id == s.ItemId && t.MinStockLevel > 0 && s.Total <= t.MinStockLevel));

        var expiringSoonCount = batches.Count(b =>
            !b.IsExpired(today) && b.DaysUntilExpiry(today) <= DefaultExpiringSoonWindowDays);
        var expiredCount = batches.Count(b => b.IsExpired(today));

        var topSelling = todayInvoices
            .SelectMany(s => s.Items)
            .GroupBy(i => i.Item.Name)
            .Select(g => new TopSellingItemDto
            {
                ItemName = g.Key,
                QuantitySold = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.LineTotal)
            })
            .OrderByDescending(t => t.QuantitySold)
            .Take(5)
            .ToList();

        return new DashboardSummaryDto
        {
            AsOfDate = today,
            TodaySalesTotal = todaySalesTotal,
            TodayInvoiceCount = todayInvoices.Count,
            TodayCogs = todayCogs,
            TodayGrossProfit = todaySalesTotal + todayCredit - todayCogs,
            TodayCreditSalesTotal = todayCredit, TodayCashSalesTotal = todayCash, TodayCardSalesTotal = todayCard,
            TodayCreditDeposits = creditDeposits, TodayDebtCollections = debtCollections, TodayCashMovement = cashMovement,
            TodayExpensesTotal = todayExpensesTotal,
            LowStockItemCount = lowStockCount,
            ExpiringSoonCount = expiringSoonCount,
            ExpiredCount = expiredCount,
            TopSellingItemsToday = topSelling
        };
    }

    public async Task<TrialBalanceDto> GetTrialBalanceAsync(DateTime asOfDate, int? branchId, CancellationToken cancellationToken = default)
    {
        var cutoff = asOfDate.Date.AddDays(1);

        var linesQuery = _context.JournalEntryLines
            .Include(l => l.Account)
            .Include(l => l.JournalEntry)
            .Where(l => l.JournalEntry.IsPosted && l.JournalEntry.EntryDate < cutoff);

        if (branchId.HasValue) linesQuery = linesQuery.Where(l => l.JournalEntry.BranchId == branchId.Value);

        var lines = await linesQuery.ToListAsync(cancellationToken);

        var grouped = lines
            .GroupBy(l => l.Account)
            .Select(g => new TrialBalanceLineDto
            {
                AccountCode = g.Key.Code,
                AccountName = g.Key.Name,
                AccountType = g.Key.Type,
                TotalDebit = g.Sum(l => l.DebitAmount),
                TotalCredit = g.Sum(l => l.CreditAmount)
            })
            .Where(l => l.TotalDebit != 0 || l.TotalCredit != 0)
            .OrderBy(l => l.AccountCode)
            .ToList();

        return new TrialBalanceDto { AsOfDate = asOfDate.Date, Lines = grouped };
    }

    public async Task<IncomeStatementDto> GetIncomeStatementAsync(DateTime fromDate, DateTime toDate, int? branchId, CancellationToken cancellationToken = default)
    {
        var rangeEnd = toDate.Date.AddDays(1);

        var invoicesQuery = _context.SalesInvoices
            .Where(s => s.Status != SalesInvoiceStatus.Voided && s.SaleAtUtc >= fromDate.Date && s.SaleAtUtc < rangeEnd);
        if (branchId.HasValue) invoicesQuery = invoicesQuery.Where(s => s.BranchId == branchId.Value);

        var invoices = await invoicesQuery
            .Include(s => s.Items).ThenInclude(i => i.BatchAllocations)
            .ToListAsync(cancellationToken);

        var totalRevenue = invoices.Sum(s => s.TotalAmount);
        var totalCogs = invoices.Sum(s => s.Items.Sum(i => i.BatchAllocations.Sum(a => a.QuantityTaken * a.UnitCost)));

        var expensesQuery = _context.Expenses
            .Include(e => e.ExpenseCategory)
            .Where(e => e.ExpenseDate >= fromDate.Date && e.ExpenseDate < rangeEnd);
        if (branchId.HasValue) expensesQuery = expensesQuery.Where(e => e.BranchId == branchId.Value);

        var expenses = await expensesQuery.ToListAsync(cancellationToken);

        var expensesByCategory = expenses
            .GroupBy(e => e.ExpenseCategory.Name)
            .Select(g => new ExpenseCategoryAmountDto { CategoryName = g.Key, Amount = g.Sum(e => e.Amount) })
            .OrderByDescending(e => e.Amount)
            .ToList();

        return new IncomeStatementDto
        {
            FromDate = fromDate.Date,
            ToDate = toDate.Date,
            TotalRevenue = totalRevenue,
            TotalCogs = totalCogs,
            ExpensesByCategory = expensesByCategory
        };
    }

    public async Task<List<InventoryMovementLineDto>> GetInventoryMovementAsync(
        DateTime fromDate, DateTime toDate, int? branchId, int? itemId, CancellationToken cancellationToken = default)
    {
        var rangeEnd = toDate.Date.AddDays(1);

        var query = _context.StockTransactions
            .Include(t => t.Item)
            .Include(t => t.Warehouse)
            .Where(t => t.TransactionAtUtc >= fromDate.Date && t.TransactionAtUtc < rangeEnd);

        if (branchId.HasValue) query = query.Where(t => t.Warehouse.BranchId == branchId.Value);
        if (itemId.HasValue) query = query.Where(t => t.ItemId == itemId.Value);

        var transactions = await query.OrderBy(t => t.TransactionAtUtc).ToListAsync(cancellationToken);

        return transactions.Select(t => new InventoryMovementLineDto
        {
            TransactionAtUtc = t.TransactionAtUtc,
            ItemCode = t.Item.Code,
            ItemName = t.Item.Name,
            WarehouseName = t.Warehouse.Name,
            Type = t.Type,
            QuantityChange = t.QuantityChange,
            ResultingQuantityOnHand = t.ResultingQuantityOnHand,
            ReferenceType = t.ReferenceType,
            Notes = t.Notes
        }).ToList();
    }

    public async Task<SalesSummaryDto> GetSalesSummaryAsync(DateTime fromDate, DateTime toDate, int? branchId, CancellationToken cancellationToken = default)
    {
        var rangeEnd = toDate.Date.AddDays(1);

        var query = _context.SalesInvoices
            .Where(s => s.Status != SalesInvoiceStatus.Voided && s.SaleAtUtc >= fromDate.Date && s.SaleAtUtc < rangeEnd);
        if (branchId.HasValue) query = query.Where(s => s.BranchId == branchId.Value);

        var invoices = await query.ToListAsync(cancellationToken);

        var byPaymentMethod = invoices
            .GroupBy(i => i.PaymentMethod)
            .Select(g => new PaymentMethodAmountDto
            {
                PaymentMethod = g.Key,
                InvoiceCount = g.Count(),
                Amount = g.Sum(i => i.TotalAmount)
            })
            .OrderByDescending(p => p.Amount)
            .ToList();

        return new SalesSummaryDto
        {
            FromDate = fromDate.Date,
            ToDate = toDate.Date,
            TotalInvoices = invoices.Count,
            GrossSales = invoices.Sum(i => i.SubTotal),
            TotalTax = invoices.Sum(i => i.TaxAmount),
            TotalDiscount = invoices.Sum(i => i.DiscountAmount),
            NetSales = invoices.Sum(i => i.TotalAmount),
            ByPaymentMethod = byPaymentMethod
        };
    }
}
