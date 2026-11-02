using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.Inventory;
using Xunit;

namespace PharmacyERP.Application.Tests;

public partial class PackagingSalesTests
{
    private PrintProductLabelsViewModel Labels(PharmacyERP.Infrastructure.Persistence.ApplicationDbContext db) =>
        new(Inventory(db), Sales(db), new BranchService(db, new PermissiveLicenseService()), new FakeCurrentUserService());

    [Fact]
    public async Task ProductLabels_UseTheSelectedBatchAndUnitPriceRatherThanTheFirstBatchPrice()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        await ReceiveAsync(db, f, sale: 1250); var expensive = await ReceiveAsync(db, f, batch: "EXPENSIVE", days: 110, sale: 2500);
        var labels = Labels(db); await labels.InitializeAsync(f.ItemId, "ITM-001", "ignored", expensive.Id);
        labels.ErrorMessage.Should().BeEmpty(); labels.SelectedBatch!.Id.Should().Be(expensive.Id);
        labels.SalePrice.Should().Be(2500); labels.Barcode.Should().Be("1234567890");
        labels.PriceNotice.Should().Contain("يختلف");
        labels.SelectedUnit = labels.Units.Single(u => u.Key == "base");
        labels.SalePrice.Should().Be(500); labels.Barcode.Should().Be("0987654321"); labels.Copies = 7;
        var request = await labels.PreparePrintAsync();
        request.Should().NotBeNull(); request!.SalePrice.Should().Be(500); request.Copies.Should().Be(7);
        request.Barcode.Should().Be("0987654321"); request.UnitName.Should().Be("شريط");
        (await db.SalesInvoices.CountAsync()).Should().Be(0); (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(20);
    }

    [Fact]
    public async Task ProductLabels_ChangedPriceRefreshesThePreviewAndRequiresAnotherPrintAttempt()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var batch = await ReceiveAsync(db, f, sale: 1250);
        var labels = Labels(db); await labels.InitializeAsync(f.ItemId, "ITM-001", "ignored", batch.Id);
        labels.SalePrice.Should().Be(1250);
        var changed = await db.Batches.SingleAsync(); changed.PackageSalePrice = 2500; changed.SalePriceOverride = 500;
        await db.SaveChangesAsync();
        (await labels.PreparePrintAsync()).Should().BeNull(); labels.ErrorMessage.Should().Contain("تغيّرت");
        labels.SalePrice.Should().Be(2500); labels.PreviewLabel!.SalePrice.Should().Be(2500);
        var reviewed = await labels.PreparePrintAsync(); reviewed.Should().NotBeNull(); reviewed!.SalePrice.Should().Be(2500);
        (await db.StockTransactions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProductLabels_DoNotInventBarcodeOrUseTheItemCodeForAnUnbarcodedUnit()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var item = await db.Items.SingleAsync(); item.Barcode = null; item.BaseUnitBarcode = null; await db.SaveChangesAsync();
        await ReceiveAsync(db, f, sale: 1250);
        var labels = Labels(db); await labels.InitializeAsync(f.ItemId, "ITM-001", "ignored");
        labels.CanPrint.Should().BeFalse(); labels.ValidationMessage.Should().Contain("لا يوجد باركود");
        (await labels.PreparePrintAsync()).Should().BeNull(); labels.ErrorMessage.Should().Contain("لا يوجد باركود");
        item.Barcode.Should().BeNull(); item.BaseUnitBarcode.Should().BeNull();
    }

    [Fact]
    public async Task ProductLabels_ExpiredSelectedBatchCannotSilentlySwitchToAnotherBatch()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var selected = await ReceiveAsync(db, f, sale: 1250); await ReceiveAsync(db, f, batch: "OTHER", days: 110, sale: 2500);
        var labels = Labels(db); await labels.InitializeAsync(f.ItemId, "ITM-001", "ignored", selected.Id);
        (await db.Batches.SingleAsync(b => b.Id == selected.Id)).ExpiryDate = _clock.UtcNow.AddDays(-1); await db.SaveChangesAsync();
        (await labels.PreparePrintAsync()).Should().BeNull(); labels.SelectedBatch.Should().BeNull();
        labels.CanPrint.Should().BeFalse(); labels.ErrorMessage.Should().Contain("دفعة صالحة"); labels.Batches.Should().ContainSingle();
    }

    [Fact]
    public async Task ProductLabels_RejectWarehouseDeactivatedAfterOpeningThePreview()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        await ReceiveAsync(db, f, sale: 1250);
        var labels = Labels(db); await labels.InitializeAsync(f.ItemId, "ITM-001", "ignored");
        labels.CanPrint.Should().BeTrue();
        (await db.Warehouses.SingleAsync()).IsActive = false; await db.SaveChangesAsync();
        (await labels.PreparePrintAsync()).Should().BeNull(); labels.ErrorMessage.Should().Contain("المخزن لم يعد نشطاً");
        labels.CanPrint.Should().BeFalse();
    }

    [Fact]
    public void ProductLabelUnits_PreserveLeadingZerosAndRejectAmbiguousDifferentUnitBarcodes()
    {
        var item = new SaleItemLookupDto { Barcode = "0012345678901", BaseUnitBarcode = "SMALL", UnitsPerPackage = 100,
            PackageUnitName = "علبة", UnitOfMeasureName = "حبة", SaleUnits = new()
            { new() { Id = 9, Name = "شريط", BaseUnitCount = 20, Barcode = "STRIP" },
              new() { Id = 10, Name = "قديم", BaseUnitCount = 10, Barcode = "OLD", IsActive = false } } };
        var units = ProductLabelSelection.UnitsFor(item);
        units.Select(u => u.Barcode).Should().Equal("0012345678901", "SMALL", "STRIP");
        units.Select(u => u.Factor).Should().Equal(100, 1, 20);
        ProductLabelSelection.BarcodeError(units[0], units).Should().BeNull();
        item.SaleUnits[0].Barcode = item.Barcode; units = ProductLabelSelection.UnitsFor(item);
        ProductLabelSelection.BarcodeError(units[0], units).Should().Contain("معاملين مختلفين");
        ProductLabelSelection.LayoutError(0, 50, 30).Should().NotBeNull();
        ProductLabelSelection.LayoutError(1, double.NaN, 30).Should().NotBeNull();
        ProductLabelSelection.LayoutError(501, 50, 30).Should().NotBeNull();
        ProductLabelSelection.LayoutError(5, 50, 30).Should().BeNull();
    }
}
