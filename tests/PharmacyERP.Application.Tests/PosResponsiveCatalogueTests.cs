using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.ViewModels.Sales;
using Xunit;

namespace PharmacyERP.Application.Tests;

public partial class PackagingSalesTests
{
    public class CatalogueGate : DispatchProxy
    {
        public ISalesService Target = null!;
        public bool HoldBrowse;
        public int BrowseCalls;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(Target, args);
            if (method.Name != nameof(ISalesService.BrowseSaleItemsAsync)) return result;
            BrowseCalls++;
            return HoldBrowse ? Delay((Task<List<SaleItemLookupDto>>)result!) : result;
        }
        private async Task<List<SaleItemLookupDto>> Delay(Task<List<SaleItemLookupDto>> task)
        {
            // Finish the actual EF operation before allowing assertions against
            // this context; only delivery of its UI result is delayed.
            var result = await task;
            Started.TrySetResult(); await Release.Task; return result;
        }
    }

    private async Task<(POSViewModel Pos, CatalogueGate Gate)> ResponsivePosAsync(ApplicationDbContext db, TestFixture f)
    {
        var sales = DispatchProxy.Create<ISalesService, CatalogueGate>(); var gate = (CatalogueGate)sales; gate.Target = Sales(db);
        var user = new FakeCurrentUserService(); user.SetSession(1, "cashier", f.BranchId, Array.Empty<string>());
        var pos = new POSViewModel(sales, new BranchService(db, new PermissiveLicenseService()), new PrescriptionService(db, _clock), user, new TestPrinter());
        await pos.InitializeAsync(); return (pos, gate);
    }

    [Fact]
    public async Task CategoryLoading_DoesNotDropQueuedScansOrAllowCheckoutBeforeTheyFinish()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1); await ReceiveAsync(db, f, boxes: 4, sale: 1250);
        var (pos, gate) = await ResponsivePosAsync(db, f); gate.HoldBrowse = true;
        var loading = pos.BrowseCategoryAsync(pos.Categories.First()); await gate.Started.Task;
        pos.IsCatalogueBusy.Should().BeTrue(); pos.IsBusy.Should().BeFalse(); pos.CanScan.Should().BeTrue();
        var first = pos.SubmitSearchAsync("1234567890", true); var second = pos.SubmitSearchAsync("1234567890", true);
        pos.CheckoutCommand.CanExecute(null).Should().BeFalse(); pos.CanLeave.Should().BeFalse();
        gate.Release.TrySetResult(); await Task.WhenAll(loading, first, second);
        pos.CartLines.Single().Quantity.Should().Be(2); pos.TotalAmount.Should().Be(2500);
        pos.IsCatalogueBusy.Should().BeFalse(); pos.ErrorMessage.Should().BeEmpty();
        (await db.SalesInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AfterSaleCatalogueRefresh_AcceptsTheNextCustomerScanWithoutAnotherSale()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1); await ReceiveAsync(db, f, boxes: 3, sale: 1250);
        var (pos, gate) = await ResponsivePosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        gate.HoldBrowse = true;
        var sale = pos.CheckoutAsync(); await gate.Started.Task;
        pos.CartLines.Should().BeEmpty(); pos.CanScan.Should().BeTrue(); pos.IsCatalogueBusy.Should().BeTrue();
        (await db.SalesInvoices.CountAsync()).Should().Be(1);
        var scan = pos.SubmitSearchAsync("1234567890", true);
        gate.Release.TrySetResult(); await Task.WhenAll(sale, scan);
        pos.CartLines.Single().Quantity.Should().Be(1); pos.TotalAmount.Should().Be(1250);
        (await db.SalesInvoices.CountAsync()).Should().Be(1);
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(2);
    }

    [Fact]
    public async Task AfterSaleCatalogueRefresh_CannotOverwriteTheNextSearch()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1); await ReceiveAsync(db, f, boxes: 3, sale: 1250);
        var (pos, gate) = await ResponsivePosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        gate.HoldBrowse = true; var sale = pos.CheckoutAsync(); await gate.Started.Task;
        pos.SearchText = "unknown new medicine"; var search = pos.RefreshSearchResultsAsync();
        gate.Release.TrySetResult(); await Task.WhenAll(sale, search);
        pos.SearchText.Should().Be("unknown new medicine"); pos.ProductCards.Should().BeEmpty(); pos.SearchResults.Should().BeEmpty();
        pos.IsCatalogueBusy.Should().BeFalse(); (await db.SalesInvoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ExplicitScan_NotifiesTheViewToCancelAnEmptySearchDebounce()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1); await ReceiveAsync(db, f, sale: 1250);
        var (pos, gate) = await ResponsivePosAsync(db, f); var cleared = 0;
        pos.SearchInputCleared += () => cleared++;
        pos.SearchText = "1234567890"; var before = gate.BrowseCalls;
        await pos.SubmitSearchAsync(pos.SearchText, true);
        pos.SearchText.Should().BeEmpty(); cleared.Should().Be(1); gate.BrowseCalls.Should().Be(before);
        pos.CartLines.Single().Quantity.Should().Be(1);
    }

    [Fact]
    public async Task SwitchingCustomer_ClearsThePreviousPrescriptionBeforeQueuedAccountQueriesRun()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1);
        var (pos, gate) = await ResponsivePosAsync(db, f); gate.HoldBrowse = true;
        var loading = pos.BrowseCategoryAsync(pos.Categories.First()); await gate.Started.Task;
        var previous = new PharmacyERP.Application.Features.Prescriptions.DTOs.ActivePrescriptionSummaryDto { Id = 99, Number = "PREVIOUS" };
        pos.FillablePrescriptions.Add(previous); pos.SelectedPrescription = previous;
        var nextCustomer = new CustomerOption { Id = 42, Name = "العميل التالي" }; pos.Customers.Add(nextCustomer);
        pos.SelectedCustomer = nextCustomer;
        pos.SelectedPrescription.Should().BeNull(); pos.FillablePrescriptions.Should().BeEmpty();
        gate.Release.TrySetResult(); await loading; await pos.PrepareToCloseAsync();
    }

    [Fact]
    public async Task LoadMore_DiscardsRowsIfTheUserStartsANewSearch()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 1);
        for (var index = 0; index < 65; index++) db.Items.Add(new Item { Code = "PAGE" + index, Name = "A product " + index,
            CategoryId = f.CategoryId, UnitOfMeasureId = f.UnitOfMeasureId });
        await db.SaveChangesAsync();
        var (pos, gate) = await ResponsivePosAsync(db, f); pos.ProductCards.Should().HaveCount(60);
        gate.HoldBrowse = true; var more = pos.LoadMoreProductsAsync(); await gate.Started.Task;
        pos.SearchText = "unknown new medicine"; var search = pos.RefreshSearchResultsAsync();
        gate.Release.TrySetResult(); await Task.WhenAll(more, search);
        pos.ProductCards.Should().BeEmpty(); pos.HasMoreProducts.Should().BeFalse(); pos.IsCatalogueBusy.Should().BeFalse();
    }
}
