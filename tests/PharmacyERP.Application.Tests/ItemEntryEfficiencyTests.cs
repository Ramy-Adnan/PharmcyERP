using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.ViewModels.Inventory;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class ItemEntryEfficiencyTests
{
    private readonly FakeDateTime _clock = new();

    [Fact]
    public async Task EmptyItemCode_IsAssignedOnceWithoutChangingTheBarcodeOrReplacingAnEnteredCode()
    {
        await using var db = TestDb.CreateContext(_clock); await TestDb.SeedBaselineAsync(db);
        var editor = new ItemEditViewModel(new InventoryService(db, _clock)); await editor.LoadForCreateAsync();
        editor.Barcode = "0012345678901"; // Incomplete name: preserve the generated identity for a corrected retry.
        await editor.SaveAsync(); editor.SavedSuccessfully.Should().BeFalse();
        var generated = editor.Code; generated.Should().MatchRegex("^ITM-[0-9A-F]{12}$");
        editor.Name = "علاج جديد"; editor.DefaultPurchasePrice = 1000; await editor.SaveAsync();
        editor.SavedSuccessfully.Should().BeTrue(); editor.Code.Should().Be(generated);
        var saved = await db.Items.SingleAsync(i => i.Id == editor.SavedItemId);
        saved.Barcode.Should().Be("0012345678901"); saved.DefaultSalePrice.Should().Be(1250);
        await editor.LoadForCreateAsync(); editor.Name = "صنف آخر"; editor.Code = "MY-CODE"; await editor.SaveAsync();
        editor.Code.Should().Be("MY-CODE");
    }

    [Fact]
    public async Task ReceiptUnitSwitch_PreservesManualSellingPrice_ButNewCostOrTypeRecalculatesIt()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var item = await db.Items.SingleAsync(); item.UnitsPerPackage = 5; item.DefaultPurchasePrice = 1000;
        db.ItemSaleUnits.Add(new ItemSaleUnit { ItemId = item.Id, Name = "شريط", BaseUnitCount = 1 }); await db.SaveChangesAsync();
        var editor = new ReceiveBatchViewModel(new InventoryService(db, _clock),
            new BranchService(db, new PermissiveLicenseService()), new FakeCurrentUserService());
        await editor.InitializeAsync(f.ItemId, f.BranchId); editor.SalePriceOverride = 1350;
        editor.SelectedPurchaseUnit = editor.PurchaseUnits.Single(u => u.Id.HasValue);
        editor.PurchasePrice.Should().Be(200); editor.SalePriceOverride.Should().Be(270);
        editor.SelectedPurchaseUnit = editor.PurchaseUnits.Single(u => !u.Id.HasValue);
        editor.PurchasePrice.Should().Be(1000); editor.SalePriceOverride.Should().Be(1350);
        editor.PurchaseType = PurchasePricingType.ByHand; editor.SalePriceOverride.Should().Be(1200);
        editor.PurchasePrice = 2000; editor.SalePriceOverride.Should().Be(2400);
    }

    [Fact]
    public async Task UnitAndManufacturerPickers_KeepCountsWithoutRetainingAllTheirItems()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var maker = new Manufacturer { Name = "الشركة" }; db.Manufacturers.Add(maker); await db.SaveChangesAsync();
        var item = await db.Items.SingleAsync(); item.ManufacturerId = maker.Id;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var inventory = new InventoryService(db, _clock);
        (await inventory.GetUnitsAsync()).Single().ItemCount.Should().Be(1);
        (await inventory.GetManufacturersAsync()).Single().ItemCount.Should().Be(1);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }
}
