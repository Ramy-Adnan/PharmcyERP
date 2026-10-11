using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.WPF.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public partial class PackagingSalesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FourUnitsFromTwoDifferentlyPricedBatches_AreChargedAndPrintedAtTheirOwnPrices(bool relational)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var interceptor = new PharmacyERP.Infrastructure.Persistence.Interceptors.AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock);
        await using ApplicationDbContext db = relational ? new SqliteTestDb(options, interceptor) : TestDb.CreateContext(_clock);
        if (relational) await db.Database.EnsureCreatedAsync();
        var f = await SeedAsync(db, 1);
        foreach (var (cost, number, days) in new[] { (1000, "OLD", 100), (2000, "NEW", 110) })
            (await Inventory(db).ReceiveBatchAsync(new() { ItemId = f.ItemId, WarehouseId = f.WarehouseId,
                Quantity = 2, PurchasePrice = cost, BatchNumber = number, ExpiryDate = _clock.UtcNow.AddDays(days) }, 1)).Succeeded.Should().BeTrue();
        if (relational)
        {
            // Exercise transactional persistence directly; SQLite cannot translate the
            // existing decimal customer aggregates used during POS initialization.
            var item = (await Sales(db).SearchSaleItemsAsync("1234567890", f.WarehouseId)).Single();
            var request = Request(f, false, 4); request.AmountTendered = 7500;
            request.Lines[0].UseBatchPricing = true;
            request.Lines[0].ExpectedPrices = PharmacyERP.Application.Features.Sales.BatchSalePricing.Quote(item.BatchPrices, 4, 1, new());
            var result = await Sales(db).CheckoutAsync(request, 1);
            result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors));
        }
        else
        {
            var pos = await PosAsync(db, f);
            for (var i = 0; i < 4; i++) await pos.SubmitSearchAsync("1234567890", true);
            pos.ErrorMessage.Should().BeEmpty();
            var row = pos.CartLines.Single(); row.Quantity.Should().Be(4);
            row.PriceSegments.Should().Equal(new SalePriceSegmentDto(2, 1250), new SalePriceSegmentDto(2, 2500));
            pos.TotalAmount.Should().Be(7500); row.HasMultipleBatchPrices.Should().BeTrue();
            await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        }
        var invoice = await db.SalesInvoices.SingleAsync(); invoice.TotalAmount.Should().Be(7500);
        var detail = (await Sales(db).GetSalesInvoiceDetailAsync(invoice.Id))!;
        detail.Lines.Select(l => (l.Quantity, l.UnitPrice)).Should().Equal((2, 1250m), (2, 2500m));
        var receipt = ReceiptContentBuilder.Build(detail, new ReceiptSettings());
        receipt.Products.Select(p => p[2].Value).Should().Equal(1250m.ToString("N2"), 2500m.ToString("N2"));
        receipt.Totals.First().Value.Should().Be($"الإجمالي: {7500m:N2} د.ع");
        (await db.SalesInvoiceItemBatches.ToListAsync()).Sum(a => a.QuantityTaken * a.UnitCost).Should().Be(6000);
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(0);
        // Return accounting contains SQL Server decimal aggregates unsupported by SQLite.
        // Its allocation/refund assertions run in the in-memory case below.
        if (relational) return;
        var expensive = detail.Lines.Single(l => l.UnitPrice == 2500);
        var returned = await Sales(db).ProcessReturnAsync(new() { SalesInvoiceId = invoice.Id, Reason = "اختبار",
            Lines = new() { new() { SalesInvoiceItemId = expensive.Id, Quantity = 2 } } }, 1);
        returned.Succeeded.Should().BeTrue(); returned.Value!.TotalAmount.Should().Be(5000);
        (await db.Batches.SingleAsync(b => b.BatchNumber == "NEW")).QuantityOnHand.Should().Be(2);
        (await db.Batches.SingleAsync(b => b.BatchNumber == "OLD")).QuantityOnHand.Should().Be(0);
    }

    [Fact]
    public async Task RelationalCheckout_UsesFreshBatchStockInsteadOfPreviouslyTrackedQuantities()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var interceptor = new PharmacyERP.Infrastructure.Persistence.Interceptors.AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock);
        await using var db = new SqliteTestDb(options, interceptor);
        await db.Database.EnsureCreatedAsync(); var f = await SeedAsync(db, 1);
        await ReceiveAsync(db, f, sale: 1250); await ReceiveAsync(db, f, batch: "NEW", days: 110, sale: 2500);
        // Model a committed stock change from another register, leaving the POS tracker stale.
        await db.Batches.Where(b => b.BatchNumber == "B1").ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, 1));
        var item = (await Sales(db).SearchSaleItemsAsync("1234567890", f.WarehouseId)).Single();
        var request = Request(f, false, 2); request.AmountTendered = 3750;
        request.Lines[0].UseBatchPricing = true;
        request.Lines[0].ExpectedPrices = PharmacyERP.Application.Features.Sales.BatchSalePricing.Quote(item.BatchPrices, 2, 1, new());
        var result = await Sales(db).CheckoutAsync(request, 1);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors));
        (await db.SalesInvoices.SingleAsync()).TotalAmount.Should().Be(3750);
        (await db.Batches.SingleAsync(b => b.BatchNumber == "B1")).QuantityOnHand.Should().Be(0);
        (await db.Batches.SingleAsync(b => b.BatchNumber == "NEW")).QuantityOnHand.Should().Be(1);
        (await db.SalesInvoiceItemBatches.ToListAsync()).Select(a => a.QuantityTaken).Should().Equal(1, 1);
    }

    [Fact]
    public async Task MixedPrices_DoNotRoundTheTotalByMultiplyingADisplayAverage()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1);
        await ReceiveAsync(db, f, sale: 1); await ReceiveAsync(db, f, boxes: 1, batch: "NEW", days: 110, sale: 2);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        pos.CartLines.Single().Quantity = 3;
        pos.CartLines.Single().UnitPrice.Should().Be(1.33m); pos.TotalAmount.Should().Be(4);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        (await db.SalesInvoices.SingleAsync()).TotalAmount.Should().Be(4);
    }

    [Fact]
    public async Task EarlierExpiryBatch_IsPricedFirstEvenWhenReceivedLater()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1);
        await ReceiveAsync(db, f, sale: 1250); await ReceiveAsync(db, f, batch: "NEW", days: 50, sale: 2500);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        pos.TotalAmount.Should().Be(2500); pos.CartLines.Single().Quantity = 3; pos.TotalAmount.Should().Be(6250);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        (await db.Batches.SingleAsync(b => b.BatchNumber == "NEW")).QuantityOnHand.Should().Be(0);
        (await db.Batches.SingleAsync(b => b.BatchNumber == "B1")).QuantityOnHand.Should().Be(1);
    }

    [Fact]
    public async Task QuantityEditsAndManualPriceOverride_RepriceAcrossBatchesWithoutChangingAnExplicitPrice()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1);
        await ReceiveAsync(db, f, sale: 1250); await ReceiveAsync(db, f, batch: "NEW", days: 110, sale: 2500);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        var row = pos.CartLines.Single(); row.Quantity = 4; pos.TotalAmount.Should().Be(7500);
        row.Quantity = 2; pos.TotalAmount.Should().Be(2500); row.HasMultipleBatchPrices.Should().BeFalse();
        row.Quantity = 3; pos.TotalAmount.Should().Be(5000);
        row.UnitPrice = 1500; row.Quantity = 4; pos.TotalAmount.Should().Be(6000); row.UsesBatchPricing.Should().BeFalse();
        row.UseAutomaticPricing(); pos.TotalAmount.Should().Be(7500);
        row.UnitPrice = 1500;
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        var line = await db.SalesInvoiceItems.SingleAsync(); line.Quantity.Should().Be(4); line.UnitPrice.Should().Be(1500);
        (await db.SalesInvoiceItemBatches.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task MixedBoxAndBaseUnits_ReserveAndReleaseTheCorrectBatchPrices()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        await ReceiveAsync(db, f, sale: 1250); await ReceiveAsync(db, f, batch: "NEW", days: 110, sale: 2500);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        var box = pos.CartLines.Single(); box.Quantity = 2;
        await pos.SubmitSearchAsync("0987654321", true);
        var strip = pos.CartLines.Single(l => !l.SellAsPackage); strip.Quantity = 2;
        strip.UnitPrice.Should().Be(500); pos.TotalAmount.Should().Be(3500);
        box.Quantity = 1; strip.UnitPrice.Should().Be(250); pos.TotalAmount.Should().Be(1750);
        box.Quantity = 3; pos.TotalAmount.Should().Be(6000);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        (await db.SalesInvoiceItems.CountAsync()).Should().Be(3);
        (await db.Batches.SingleAsync(b => b.BatchNumber == "NEW")).QuantityOnHand.Should().Be(3);
    }

    [Fact]
    public async Task ABoxDrawnFromTwoPartialBatches_UsesEachContributionAndReturnsToBoth()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        await ReceiveAsync(db, f, boxes: 1, sale: 1250);
        await Inventory(db).IssueStockFefoAsync(f.ItemId, f.WarehouseId, 2, "Test", null, null);
        await ReceiveAsync(db, f, boxes: 1, batch: "NEW", days: 110, sale: 2500);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        pos.TotalAmount.Should().Be(1750); pos.CartLines.Single().UnitsPerSale.Should().Be(5);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        var line = await db.SalesInvoiceItems.SingleAsync(); line.UnitPrice.Should().Be(1750);
        (await db.SalesInvoiceItemBatches.CountAsync()).Should().Be(2);
        var result = await Sales(db).ProcessReturnAsync(new() { SalesInvoiceId = line.SalesInvoiceId, Reason = "اختبار",
            Lines = new() { new() { SalesInvoiceItemId = line.Id, Quantity = 1 } } }, 1);
        result.Succeeded.Should().BeTrue(); result.Value!.TotalAmount.Should().Be(1750);
        (await db.Batches.SingleAsync(b => b.BatchNumber == "B1")).QuantityOnHand.Should().Be(3);
        (await db.Batches.SingleAsync(b => b.BatchNumber == "NEW")).QuantityOnHand.Should().Be(5);
    }

    [Fact]
    public async Task ChangedBatchAvailability_RequiresReviewAndRefreshesTheBasketBeforeRetry()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1);
        await ReceiveAsync(db, f, sale: 1250); await ReceiveAsync(db, f, batch: "NEW", days: 110, sale: 2500);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        pos.CartLines.Single().Quantity = 2; pos.TotalAmount.Should().Be(2500);
        await Inventory(db).IssueStockFefoAsync(f.ItemId, f.WarehouseId, 1, "OtherRegister", null, null);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().Contain("تغيّرت"); pos.TotalAmount.Should().Be(3750);
        pos.CheckoutUncertain.Should().BeFalse(); (await db.SalesInvoices.CountAsync()).Should().Be(0);
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(3);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        (await db.SalesInvoices.SingleAsync()).TotalAmount.Should().Be(3750);
    }

    [Fact]
    public async Task BatchPriceSegments_KeepDiscountsTaxesAndCreditDebtConsistent()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1);
        (await db.Items.SingleAsync()).TaxRatePercent = 10; await db.SaveChangesAsync();
        await ReceiveAsync(db, f, sale: 1250); await ReceiveAsync(db, f, batch: "NEW", days: 110, sale: 2500);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        pos.CartLines.Single().Quantity = 4; pos.CartLines.Single().DiscountAmount = 300;
        pos.DiscountAmount = 50; pos.PaymentMethod = PaymentMethod.Credit;
        pos.SelectedCustomer = pos.Customers.Single(c => c.Id.HasValue); pos.AmountTendered = 1000;
        pos.TotalAmount.Should().Be(7900);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        var invoice = await db.SalesInvoices.SingleAsync(); invoice.TotalAmount.Should().Be(7900); invoice.TaxAmount.Should().Be(750);
        var lines = await db.SalesInvoiceItems.OrderBy(l => l.UnitPrice).ToListAsync();
        lines.Select(l => l.DiscountAmount).Should().Equal(100m, 200m);
        (await Sales(db).GetCustomerAccountAsync(pos.SelectedCustomer!.Id!.Value))!.OutstandingAmount.Should().Be(6900);
    }
}
