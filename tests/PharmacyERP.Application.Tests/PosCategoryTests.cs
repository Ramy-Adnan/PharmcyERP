using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.Sales;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class PosCategoryTests
{
    private readonly FakeDateTime _clock = new();
    private SalesService Sales(ApplicationDbContext db) => new(db, new InventoryService(db, _clock), new AccountingService(db, _clock), _clock, new FakeCurrentUserService());
    private async Task<POSViewModel> PosAsync(ApplicationDbContext db, TestFixture fixture, PharmacyERP.Application.Features.Sales.ISalesService? service = null)
    {
        var user = new FakeCurrentUserService(); user.SetSession(1, "cashier", fixture.BranchId, Array.Empty<string>());
        var pos = new POSViewModel(service ?? Sales(db), new BranchService(db, new PermissiveLicenseService()), new PrescriptionService(db, _clock), user,
            new Printer(), new InventoryService(db, _clock));
        await pos.InitializeAsync(); return pos;
    }
    private sealed class Printer : IReceiptPrinter { public void Print(SalesInvoiceDetailDto invoice) { } }

    [Fact]
    public async Task LiveSearch_UpdatesCardsWithoutAddingOrClearingText_AndClearingRestoresCatalogue()
    {
        await using var db = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(db);
        await TestDb.AddBatchAsync(db, fixture, "FRESH", _clock.UtcNow.AddDays(100), 10);
        var pos = await PosAsync(db, fixture);
        pos.SearchText = "ITM-001";
        await pos.RefreshSearchResultsAsync();
        pos.ProductCards.Should().ContainSingle(c => c.Item.ItemId == fixture.ItemId);
        pos.CartLines.Should().BeEmpty();
        pos.SearchText.Should().Be("ITM-001");
        pos.SearchText = "no such medicine";
        await pos.RefreshSearchResultsAsync();
        pos.ProductCards.Should().BeEmpty();
        pos.SearchText = "";
        await pos.RefreshSearchResultsAsync();
        pos.ProductCards.Should().ContainSingle();
        pos.Categories.First().IsSelected.Should().BeTrue();
        await pos.SubmitSearchAsync("ITM-001");
        pos.CartLines.Should().ContainSingle();
    }

    [Fact]
    public async Task LiveSearch_DiscardsResultsWhenTextChangesDuringAnOutstandingQuery()
    {
        await using var db = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(db);
        var proxy = System.Reflection.DispatchProxy.Create<PharmacyERP.Application.Features.Sales.ISalesService, DelayedSearch>();
        var gate = (DelayedSearch)proxy; gate.Target = Sales(db);
        var pos = await PosAsync(db, fixture, proxy);
        var added = new List<POSProductCard>();
        pos.ProductCards.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) added.AddRange(e.NewItems.Cast<POSProductCard>());
        };
        pos.SearchText = "ITM-001";
        var oldQuery = pos.RefreshSearchResultsAsync();
        await gate.Started.Task;
        pos.SearchText = "no such medicine";
        var newQuery = pos.RefreshSearchResultsAsync();
        gate.Release.TrySetResult();
        await Task.WhenAll(oldQuery, newQuery);
        added.Should().BeEmpty();
        pos.ProductCards.Should().BeEmpty();
        pos.CartLines.Should().BeEmpty();
        pos.SearchText.Should().Be("no such medicine");
    }

    [Fact]
    public async Task Browse_FiltersBeforeLimit_AndExcludesInactiveProducts()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await TestDb.SeedBaselineAsync(db);
        var category = new ItemCategory { Code = "SKIN", Name = "عناية بالبشرة" }; db.ItemCategories.Add(category); await db.SaveChangesAsync();
        for (var i = 0; i < 70; i++) db.Items.Add(new Item { Code = "OTHER" + i, Name = "A other " + i, CategoryId = fixture.CategoryId, UnitOfMeasureId = fixture.UnitOfMeasureId });
        db.Items.Add(new Item { Code = "SERUM", Name = "Z Serum", CategoryId = category.Id, UnitOfMeasureId = fixture.UnitOfMeasureId, DefaultSalePrice = 15000 });
        db.Items.Add(new Item { Code = "INACTIVE", Name = "Old serum", CategoryId = category.Id, UnitOfMeasureId = fixture.UnitOfMeasureId, IsActive = false });
        await db.SaveChangesAsync();
        var results = await Sales(db).BrowseSaleItemsAsync(fixture.WarehouseId, category.Id);
        results.Should().ContainSingle().Which.Code.Should().Be("SERUM");
        (await Sales(db).BrowseSaleItemsAsync(fixture.WarehouseId)).Should().HaveCount(60);
        var first = await Sales(db).BrowseSaleItemsAsync(fixture.WarehouseId);
        var second = await Sales(db).BrowseSaleItemsAsync(fixture.WarehouseId, skip: 60);
        second.Should().HaveCount(12);
        first.Concat(second).Select(i => i.ItemId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task CategorySelection_DoesNotAddToCart_AndDoesNotRestrictScanner()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await TestDb.SeedBaselineAsync(db);
        var item = await db.Items.SingleAsync(); item.Barcode = "0123456789012";
        var category = new ItemCategory { Code = "SKIN", Name = "عناية بالبشرة" };
        db.ItemCategories.Add(category); db.ItemCategories.Add(new ItemCategory { Code = "OLD", Name = "موقوف", IsActive = false });
        await db.SaveChangesAsync();
        db.Items.Add(new Item { Code = "SERUM", Name = "Serum", CategoryId = category.Id, UnitOfMeasureId = fixture.UnitOfMeasureId });
        await TestDb.AddBatchAsync(db, fixture, "VALID", _clock.UtcNow.AddDays(100), 10);
        var pos = await PosAsync(db, fixture);
        pos.Categories.Select(c => c.Name).Should().BeEquivalentTo("جميع الأصناف", "عام", "عناية بالبشرة");
        var tile = pos.Categories.Single(c => c.Id == category.Id);
        await pos.BrowseCategoryAsync(tile);
        pos.SearchResults.Should().ContainSingle().Which.Code.Should().Be("SERUM");
        pos.CartLines.Should().BeEmpty(); tile.IsSelected.Should().BeTrue();
        await pos.SubmitSearchAsync(item.Barcode, barcodeOnly: true);
        await pos.SubmitSearchAsync(item.Barcode, barcodeOnly: true);
        pos.CartLines.Should().ContainSingle().Which.Quantity.Should().Be(2);
        pos.CartLines.Single().ItemId.Should().Be(fixture.ItemId);
        tile.IsSelected.Should().BeTrue(); pos.SearchResults.Should().BeEmpty();
        pos.ProductCards.Should().ContainSingle().Which.Item.Code.Should().Be("SERUM");
    }

    [Fact]
    public async Task Cancel_ResetsAmountsAndCart_WithoutRecordingSaleOrChangingStock()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await TestDb.SeedBaselineAsync(db);
        await TestDb.AddBatchAsync(db, fixture, "VALID", _clock.UtcNow.AddDays(100), 10);
        var pos = await PosAsync(db, fixture); await pos.SubmitSearchAsync("ITM-001");
        pos.PaymentMethod = PaymentMethod.Credit; pos.AmountTendered = 5; pos.DiscountAmount = 2;
        pos.CancelTransactionCommand.CanExecute(null).Should().BeTrue();
        pos.CancelTransactionCommand.Execute(null);
        pos.CartLines.Should().BeEmpty(); pos.AmountTendered.Should().Be(0); pos.DiscountAmount.Should().Be(0); pos.TotalAmount.Should().Be(0);
        (await db.SalesInvoices.CountAsync()).Should().Be(0); (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(10);
    }

    [Fact]
    public async Task RelationalBrowse_UsesRealCategoryAndExcludesExpiredStock()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var interceptor = new PharmacyERP.Infrastructure.Persistence.Interceptors.AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock);
        await using var db = new SqliteTestDb(options, interceptor); await db.Database.EnsureCreatedAsync();
        var fixture = await TestDb.SeedBaselineAsync(db);
        await TestDb.AddBatchAsync(db, fixture, "VALID", _clock.UtcNow.AddDays(100), 5);
        await TestDb.AddBatchAsync(db, fixture, "EXPIRED", _clock.UtcNow.AddDays(-1), 20);
        var categories = await new InventoryService(db, _clock).GetCategoriesAsync();
        categories.Should().ContainSingle().Which.ItemCount.Should().Be(1);
        var lookup = await Sales(db).BrowseSaleItemsAsync(fixture.WarehouseId, fixture.CategoryId);
        lookup.Should().ContainSingle().Which.AvailableQuantity.Should().Be(5);
        (await Sales(db).BrowseSaleItemsAsync(fixture.WarehouseId)).Should().ContainSingle();
        (await Sales(db).BrowseSaleItemsAsync(fixture.WarehouseId, fixture.CategoryId + 100)).Should().BeEmpty();
    }

    [Fact]
    public async Task CustomerProfile_IsForRegisteredCustomers_AndRetainsTheCurrentCart()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await TestDb.SeedBaselineAsync(db);
        db.Customers.Add(new Customer { Code = "C1", Name = "Ahmed", IsActive = true }); await db.SaveChangesAsync();
        await TestDb.AddBatchAsync(db, fixture, "VALID", _clock.UtcNow.AddDays(100), 10);
        var pos = await PosAsync(db, fixture); await pos.SubmitSearchAsync("ITM-001");
        var called = 0;
        await pos.ShowCustomerProfileAsync(_ => { called++; return Task.CompletedTask; }); called.Should().Be(0);
        pos.SelectedCustomer = pos.Customers.Single(c => c.Id.HasValue);
        await pos.ShowCustomerProfileAsync(id => { id.Should().Be(pos.SelectedCustomer.Id); called++; pos.IsBusy.Should().BeTrue(); return Task.CompletedTask; });
        called.Should().Be(1); pos.CartLines.Should().ContainSingle(); pos.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task ProductCard_AddsTheDisplayedUnitAndPrice_AfterTheBasketUnitIsChanged()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await TestDb.SeedBaselineAsync(db);
        var item = await db.Items.SingleAsync(); item.UnitsPerPackage = 5; item.DefaultSalePrice = 10000; item.PackageUnitName = "علبة";
        await db.SaveChangesAsync(); await TestDb.AddBatchAsync(db, fixture, "FRESH", _clock.UtcNow.AddDays(100), 10);
        var pos = await PosAsync(db, fixture); var card = pos.ProductCards.Single();
        card.Price.Should().Be(10000); card.UnitName.Should().Be("علبة");
        pos.AddProductCommand.Execute(card);
        pos.CartLines.Single().UnitPrice.Should().Be(card.Price);
        var line = pos.CartLines.Single(); line.SelectedSaleUnit = line.SaleUnits.First();
        line.UnitPrice.Should().Be(2000);
        pos.AddProductCommand.Execute(card);
        pos.CartLines.Should().HaveCount(2); pos.CartLines.Single(l => l.SellAsPackage).UnitPrice.Should().Be(card.Price);
        pos.TotalAmount.Should().Be(12000);
        await pos.PrepareToCloseAsync(); pos.CanScan.Should().BeFalse();
        pos.CheckoutCommand.CanExecute(null).Should().BeFalse();
        await pos.CheckoutAsync();
        await pos.SubmitSearchAsync("ITM-001"); pos.TotalAmount.Should().Be(12000);
        (await db.SalesInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Pos_OffersOnlyCashAndCredit_AndRejectsCardSelection()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await TestDb.SeedBaselineAsync(db);
        var pos = await PosAsync(db, fixture);
        pos.PaymentMethods.Select(m => m.Value).Should().BeEquivalentTo(new[] { PaymentMethod.Cash, PaymentMethod.Credit });
        pos.PaymentMethod = PaymentMethod.Card;
        pos.PaymentMethod.Should().Be(PaymentMethod.Cash); pos.ErrorMessage.Should().Contain("النقدي والآجل");
    }

    public class DelayedSearch : System.Reflection.DispatchProxy
    {
        public PharmacyERP.Application.Features.Sales.ISalesService Target = null!;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(Target, args);
            return method.Name == "SearchSaleItemsAsync" ? DelayAsync((Task<List<SaleItemLookupDto>>)result!) : result;
        }
        private async Task<List<SaleItemLookupDto>> DelayAsync(Task<List<SaleItemLookupDto>> result)
        { Started.TrySetResult(); await Release.Task; return await result; }
    }

    [Fact]
    public async Task Closing_WaitsForAnOutstandingScanAndStopsNewScans()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await TestDb.SeedBaselineAsync(db);
        await TestDb.AddBatchAsync(db, fixture, "FRESH", _clock.UtcNow.AddDays(100), 10);
        var proxy = System.Reflection.DispatchProxy.Create<PharmacyERP.Application.Features.Sales.ISalesService, DelayedSearch>();
        var gate = (DelayedSearch)proxy; gate.Target = Sales(db);
        var pos = await PosAsync(db, fixture, proxy); var scan = pos.SubmitSearchAsync("ITM-001");
        await gate.Started.Task; pos.CanLeave.Should().BeFalse();
        var close = pos.PrepareToCloseAsync(); close.IsCompleted.Should().BeFalse();
        gate.Release.TrySetResult(); await Task.WhenAll(scan, close);
        pos.CanScan.Should().BeFalse(); pos.CartLines.Should().ContainSingle();
        await pos.SubmitSearchAsync("ITM-001"); pos.CartLines.Single().Quantity.Should().Be(1);
    }
}
