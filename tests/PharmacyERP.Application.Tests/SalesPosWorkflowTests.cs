using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class SalesPosWorkflowTests
{
    private readonly FakeDateTime _clock = new();
    private SalesService Sales(ApplicationDbContext db) => new(db, new InventoryService(db, _clock), new AccountingService(db, _clock), _clock, new FakeCurrentUserService());
    private async Task<(TestFixture Fixture, int CustomerId, int UserId)> SeedAsync(ApplicationDbContext db)
    {
        var f = await TestDb.SeedBaselineAsync(db);
        await TestDb.AddBatchAsync(db, f, "FRESH", _clock.UtcNow.AddMonths(3), 20);
        db.Roles.Add(new Role { Name = "Cashier" });
        await db.SaveChangesAsync();
        var user = new User { Username = "cashier", FullName = "Cashier", PasswordHash = "test-hash", RoleId = await db.Roles.Select(r => r.Id).FirstAsync(), DefaultBranchId = f.BranchId };
        var customer = new Customer { Code = "C1", Name = "Registered customer", IsActive = true };
        db.Users.Add(user); db.Customers.Add(customer);
        foreach (var code in new[] { "1110", "1120", "1160", "4100", "2200" })
            db.ChartOfAccounts.Add(new ChartOfAccount { Code = code, Name = code, Type = code == "4100" ? AccountType.Revenue : AccountType.Asset, IsActive = true });
        await db.SaveChangesAsync();
        return (f, customer.Id, user.Id);
    }
    private static SalesCheckoutDto Checkout(TestFixture f, PaymentMethod method, int? customer = null, int quantity = 2) => new()
    {
        BranchId = f.BranchId, WarehouseId = f.WarehouseId, CustomerId = customer,
        PaymentMethod = method, AmountTendered = method == PaymentMethod.Cash ? 100 : 0,
        Lines = new() { new() { ItemId = f.ItemId, Quantity = quantity, UnitPrice = 10 } }
    };

    [Theory]
    [InlineData(PaymentMethod.Cash, "1110", 100)]
    [InlineData(PaymentMethod.Card, "1120", 20)]
    [InlineData(PaymentMethod.Credit, "1160", 0)]
    public async Task Checkout_PostsTheActualPaymentMethod(PaymentMethod method, string account, int tendered)
    {
        await using var db = TestDb.CreateContext(_clock);
        var seed = await SeedAsync(db);
        var result = await Sales(db).CheckoutAsync(Checkout(seed.Fixture, method, seed.CustomerId), seed.UserId);
        result.Succeeded.Should().BeTrue();
        result.Value!.AmountTendered.Should().Be(tendered);
        var lines = await db.JournalEntryLines.Include(l => l.Account).ToListAsync();
        lines.Where(l => l.Account.Code == account).Sum(l => l.DebitAmount).Should().Be(20);
        lines.Sum(l => l.DebitAmount).Should().Be(lines.Sum(l => l.CreditAmount));
        if (method != PaymentMethod.Cash) lines.Where(l => l.Account.Code == "1110").Should().BeEmpty();
    }

    [Fact]
    public async Task Credit_RequiresAnActiveRegisteredCustomerBeforeMutatingStock()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db);
        foreach (var customerId in new int?[] { null, 99999 })
            (await Sales(db).CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Credit, customerId), seed.UserId)).Succeeded.Should().BeFalse();
        (await db.Customers.FirstAsync()).IsActive = false; await db.SaveChangesAsync();
        (await Sales(db).CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Credit, seed.CustomerId), seed.UserId)).Succeeded.Should().BeFalse();
        (await db.SalesInvoices.CountAsync()).Should().Be(0);
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(20);
    }

    [Fact]
    public async Task Checkout_RetryDoesNotCreateAnotherSaleOrIssueMoreStock()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db); var sales = Sales(db);
        var request = Checkout(seed.Fixture, PaymentMethod.Cash);
        var first = await sales.CheckoutAsync(request, seed.UserId); var retry = await sales.CheckoutAsync(request, seed.UserId);
        retry.Value!.Id.Should().Be(first.Value!.Id);
        (await db.SalesInvoices.CountAsync()).Should().Be(1);
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(18);
        (await db.JournalEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PartialDebtPayment_ReducesCustomerDebtWithoutRecordingRevenueAgain()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db); var sales = Sales(db);
        var sale = await sales.CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Credit, seed.CustomerId), seed.UserId);
        var payment = new CustomerDebtPaymentDto { CustomerId = seed.CustomerId, InvoiceId = sale.Value!.Id, Amount = 7 };
        (await sales.RecordCustomerPaymentAsync(payment)).Succeeded.Should().BeTrue();
        (await sales.RecordCustomerPaymentAsync(payment)).Succeeded.Should().BeTrue(); // safe retry
        var account = await sales.GetCustomerAccountAsync(seed.CustomerId);
        account!.OutstandingAmount.Should().Be(13); account.Payments.Should().HaveCount(1);
        (await sales.GetCustomersAsync()).Single().OutstandingAmount.Should().Be(13);
        var lines = await db.JournalEntryLines.Include(l => l.Account).ToListAsync();
        lines.Where(l => l.Account.Code == "4100").Sum(l => l.CreditAmount).Should().Be(20);
        lines.Where(l => l.Account.Code == "1160").Sum(l => l.DebitAmount - l.CreditAmount).Should().Be(13);
        lines.Where(l => l.Account.Code == "1110").Sum(l => l.DebitAmount).Should().Be(7);
    }

    [Fact]
    public async Task DebtPayment_RejectsOverpaymentWrongCustomerAndCreditMethod()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db); var sales = Sales(db);
        var sale = await sales.CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Credit, seed.CustomerId), seed.UserId);
        foreach (var request in new[] {
            new CustomerDebtPaymentDto { CustomerId = seed.CustomerId, InvoiceId = sale.Value!.Id, Amount = 21 },
            new CustomerDebtPaymentDto { CustomerId = 99999, InvoiceId = sale.Value!.Id, Amount = 1 },
            new CustomerDebtPaymentDto { CustomerId = seed.CustomerId, InvoiceId = sale.Value!.Id, Amount = 1, PaymentMethod = PaymentMethod.Credit } })
            (await sales.RecordCustomerPaymentAsync(request)).Succeeded.Should().BeFalse();
        (await db.Receipts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreditReturn_ReducesDebtAndReversesTaxAndDiscountsProportionally()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db); var sales = Sales(db);
        var request = Checkout(seed.Fixture, PaymentMethod.Credit, seed.CustomerId); request.DiscountAmount = 2;
        request.Lines[0].TaxRatePercent = 10; request.Lines[0].DiscountAmount = 2;
        var sale = await sales.CheckoutAsync(request, seed.UserId); sale.Value!.TotalAmount.Should().Be(18);
        var line = await db.SalesInvoiceItems.SingleAsync();
        var returned = await sales.ProcessReturnAsync(new SalesReturnRequestDto { SalesInvoiceId = sale.Value.Id, Reason = "Test", Lines = new() { new() { SalesInvoiceItemId = line.Id, Quantity = 1 } } }, seed.UserId);
        returned.Succeeded.Should().BeTrue(); returned.Value!.TotalAmount.Should().Be(9);
        (await sales.GetCustomerAccountAsync(seed.CustomerId))!.OutstandingAmount.Should().Be(9);
        var entries = await db.JournalEntries.Include(j => j.Lines).ToListAsync();
        entries.Should().AllSatisfy(e => e.Lines.Sum(l => l.DebitAmount).Should().Be(e.Lines.Sum(l => l.CreditAmount)));
        var second = await sales.ProcessReturnAsync(new SalesReturnRequestDto { SalesInvoiceId = sale.Value.Id, Reason = "Test", Lines = new() { new() { SalesInvoiceItemId = line.Id, Quantity = 1 } } }, seed.UserId);
        second.Value!.TotalAmount.Should().Be(9);
        (await sales.GetCustomerAccountAsync(seed.CustomerId))!.OutstandingAmount.Should().Be(0);
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(20);
    }

    [Fact]
    public async Task PaidCreditInvoice_CannotBeVoidedOrRefundedBeyondItsUnpaidDebt()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db); var sales = Sales(db);
        var sale = await sales.CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Credit, seed.CustomerId), seed.UserId);
        await sales.RecordCustomerPaymentAsync(new() { CustomerId = seed.CustomerId, InvoiceId = sale.Value!.Id, Amount = 15 });
        (await sales.CancelSalesInvoiceAsync(sale.Value.Id, seed.UserId)).Succeeded.Should().BeFalse();
        var line = await db.SalesInvoiceItems.SingleAsync();
        (await sales.ProcessReturnAsync(new() { SalesInvoiceId = sale.Value.Id, Reason = "Test", Lines = new() { new() { SalesInvoiceItemId = line.Id, Quantity = 1 } } }, seed.UserId)).Succeeded.Should().BeFalse();
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(18);
    }

    [Fact]
    public async Task LegacyCreditReclassification_IsAuditableAndIdempotent()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db); var sales = Sales(db);
        var sale = await sales.CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Credit, seed.CustomerId), seed.UserId);
        var receivableLine = await db.JournalEntryLines.Include(l => l.Account).SingleAsync(l => l.Account.Code == "1160");
        receivableLine.AccountId = await db.ChartOfAccounts.Where(a => a.Code == "1110").Select(a => a.Id).SingleAsync();
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); // emulate the old cash-posting bug
        (await sales.ReconcileCustomerCreditAsync(seed.CustomerId)).Succeeded.Should().BeTrue();
        (await sales.ReconcileCustomerCreditAsync(seed.CustomerId)).Succeeded.Should().BeTrue();
        (await db.JournalEntries.CountAsync(j => j.ReferenceType == "CustomerCreditReclass")).Should().Be(1);
        var lines = await db.JournalEntryLines.Include(l => l.Account).ToListAsync();
        lines.Where(l => l.Account.Code == "1110").Sum(l => l.DebitAmount - l.CreditAmount).Should().Be(0);
        lines.Where(l => l.Account.Code == "1160").Sum(l => l.DebitAmount - l.CreditAmount).Should().Be(20);
    }

    [Fact]
    public async Task Dashboard_SeparatesCreditFromPaidSalesAndDoesNotCountDebtCollectionAsSale()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db); var sales = Sales(db);
        await sales.CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Cash), seed.UserId);
        await sales.CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Card), seed.UserId);
        var credit = await sales.CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Credit, seed.CustomerId), seed.UserId);
        await sales.RecordCustomerPaymentAsync(new() { CustomerId = seed.CustomerId, InvoiceId = credit.Value!.Id, Amount = 5 });
        var summary = await new ReportingService(db, _clock).GetDashboardSummaryAsync(seed.Fixture.BranchId);
        summary.TodaySalesTotal.Should().Be(40); summary.TodayCreditSalesTotal.Should().Be(20);
        summary.TodayCashSalesTotal.Should().Be(20); summary.TodayCardSalesTotal.Should().Be(20);
        summary.TodayDebtCollections.Should().Be(5); summary.TodayNetCashPosition.Should().Be(25);
    }

    [Fact]
    public async Task BarcodeLookup_FindsExactMatchBeforeThirtySimilarBarcodesAndIgnoresExpiredStock()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db);
        var item = await db.Items.SingleAsync(); item.Barcode = "12345";
        for (var i = 0; i < 35; i++) db.Items.Add(new Item { Code = "I" + i, Name = "Similar", Barcode = "12345" + i,
            CategoryId = seed.Fixture.CategoryId, UnitOfMeasureId = seed.Fixture.UnitOfMeasureId, IsActive = true });
        await TestDb.AddBatchAsync(db, seed.Fixture, "EXPIRED", _clock.UtcNow.AddDays(-1), 100);
        await db.SaveChangesAsync();
        var results = await Sales(db).SearchSaleItemsAsync("12345", seed.Fixture.WarehouseId);
        results.First().ItemId.Should().Be(item.Id); results.First().AvailableQuantity.Should().Be(20);
        var sale = await Sales(db).CheckoutAsync(Checkout(seed.Fixture, PaymentMethod.Cash, quantity: 21), seed.UserId);
        sale.Succeeded.Should().BeFalse(); (await db.SalesInvoices.CountAsync()).Should().Be(0);
    }
    private sealed class TestPrinter : PharmacyERP.WPF.Services.IReceiptPrinter
    {
        public bool Fail { get; set; }
        public List<int> PrintedIds { get; } = new();
        public void Print(SalesInvoiceDetailDto invoice)
        {
            if (Fail) throw new InvalidOperationException("Printer offline");
            PrintedIds.Add(invoice.Header.Id);
        }
    }
    private async Task<(PharmacyERP.WPF.ViewModels.Sales.POSViewModel Pos, TestPrinter Printer)> PosAsync(ApplicationDbContext db, TestFixture fixture, PharmacyERP.Application.Features.Sales.ISalesService? serviceOverride = null)
    {
        var user = new FakeCurrentUserService();
        user.SetSession(1, "cashier", fixture.BranchId, Array.Empty<string>());
        var printer = new TestPrinter();
        var pos = new PharmacyERP.WPF.ViewModels.Sales.POSViewModel(serviceOverride ?? Sales(db), new BranchService(db, new PermissiveLicenseService()),
            new PrescriptionService(db, _clock), user, printer);
        await pos.InitializeAsync();
        return (pos, printer);
    }

    [Fact]
    public async Task Pos_RapidRepeatedBarcodeScansIncrementOneLineAndRefreshEditedTotals()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db);
        (await db.Items.SingleAsync()).Barcode = "0123456789012"; await db.SaveChangesAsync();
        var (pos, _) = await PosAsync(db, seed.Fixture);
        await Task.WhenAll(pos.SubmitSearchAsync("0123456789012", true), pos.SubmitSearchAsync("0123456789012", true));
        pos.CartLines.Should().HaveCount(1); pos.CartLines[0].Quantity.Should().Be(2);
        pos.SearchText.Should().BeEmpty(); pos.TotalAmount.Should().Be(20);
        var notifications = new List<string?>(); pos.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        pos.CartLines[0].Quantity = 3; pos.CartLines[0].DiscountAmount = 2;
        notifications.Should().Contain("TotalAmount"); pos.TotalAmount.Should().Be(28);
    }

    [Fact]
    public async Task Pos_BarcodePrefixDoesNotAddAnUnrelatedItemOrExceedStock()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db);
        (await db.Items.SingleAsync()).Barcode = "0123456789012"; await db.SaveChangesAsync();
        var (pos, _) = await PosAsync(db, seed.Fixture);
        await pos.SubmitSearchAsync("012345", true); pos.CartLines.Should().BeEmpty();
        for (var i = 0; i < 21; i++) await pos.SubmitSearchAsync("0123456789012", true);
        pos.CartLines.Single().Quantity.Should().Be(20); pos.ErrorMessage.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Pos_PrinterFailureClearsTheCommittedCartAndReprintDoesNotSellAgain()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db);
        var (pos, printer) = await PosAsync(db, seed.Fixture);
        await pos.SubmitSearchAsync("ITM-001"); pos.ExactCashCommand.Execute(null); printer.Fail = true;
        await pos.CheckoutAsync();
        pos.CartLines.Should().BeEmpty(); pos.ErrorMessage.Should().Contain("تم البيع وحفظ الفاتورة");
        (await db.SalesInvoices.CountAsync()).Should().Be(1);
        await pos.CheckoutAsync(); // empty cart cannot repeat sale after a print failure
        printer.Fail = false; await pos.ReprintAsync();
        printer.PrintedIds.Should().HaveCount(1); (await db.SalesInvoices.CountAsync()).Should().Be(1);
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(19);
    }

    [Fact]
    public async Task RelationalCheckout_RollsBackInvoiceAndStockWhenAccountingFails()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var interceptor = new PharmacyERP.Infrastructure.Persistence.Interceptors.AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock);
        await using var db = new SqliteTestDb(options, interceptor);
        await db.Database.EnsureCreatedAsync();
        var seed = await SeedAsync(db);
        // Make accounting fail only after the invoice and FEFO stock issue have been written.
        db.ChartOfAccounts.Remove(await db.ChartOfAccounts.SingleAsync(a => a.Code == "2200"));
        await db.SaveChangesAsync();
        var request = Checkout(seed.Fixture, PaymentMethod.Cash);
        await FluentActions.Awaiting(() => Sales(db).CheckoutAsync(request, seed.UserId)).Should().ThrowAsync<InvalidOperationException>();
        (await db.SalesInvoices.CountAsync()).Should().Be(0);
        (await db.SalesInvoiceItems.CountAsync()).Should().Be(0);
        (await db.StockTransactions.CountAsync()).Should().Be(0);
        (await db.JournalEntries.CountAsync()).Should().Be(0);
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(20);
        // Restore the missing prerequisite, then prove the same request can succeed exactly once.
        (await db.ChartOfAccounts.IgnoreQueryFilters().SingleAsync(a => a.Code == "2200")).IsDeleted = false;
        await db.SaveChangesAsync();
        var result = await Sales(db).CheckoutAsync(request, seed.UserId);
        result.Succeeded.Should().BeTrue();
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(18);
        (await db.SalesInvoices.CountAsync()).Should().Be(1);
    }

    public class LostCheckoutResponse : System.Reflection.DispatchProxy
    {
        public PharmacyERP.Application.Features.Sales.ISalesService Target = null!;
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(Target, args);
            return method.Name == "CheckoutAsync" ? LoseResponseAsync((Task<PharmacyERP.Application.Common.Models.Result<SalesInvoiceDto>>)result!) : result;
        }
        private static async Task<PharmacyERP.Application.Common.Models.Result<SalesInvoiceDto>> LoseResponseAsync(Task<PharmacyERP.Application.Common.Models.Result<SalesInvoiceDto>> task)
        { await task; throw new IOException("Connection lost after server commit"); }
    }

    [Fact]
    public async Task Pos_LostCheckoutResponseIsResolvedByReadingTheSavedInvoiceWithoutAnotherSale()
    {
        await using var db = TestDb.CreateContext(_clock); var seed = await SeedAsync(db);
        var proxy = System.Reflection.DispatchProxy.Create<PharmacyERP.Application.Features.Sales.ISalesService, LostCheckoutResponse>();
        ((LostCheckoutResponse)proxy).Target = Sales(db);
        var (pos, printer) = await PosAsync(db, seed.Fixture, proxy);
        await pos.SubmitSearchAsync("ITM-001"); pos.ExactCashCommand.Execute(null);
        await pos.CheckoutAsync(); pos.CheckoutUncertain.Should().BeTrue(); pos.CanScan.Should().BeFalse();
        await pos.CheckoutAsync(); (await db.SalesInvoices.CountAsync()).Should().Be(1);
        await pos.ResolveCheckoutAsync(); pos.CheckoutUncertain.Should().BeFalse(); pos.CartLines.Should().BeEmpty();
        printer.PrintedIds.Should().HaveCount(1); (await db.SalesInvoices.CountAsync()).Should().Be(1);
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(19);
    }

}
