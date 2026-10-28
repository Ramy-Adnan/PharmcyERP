using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.ViewModels.Inventory;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class QuickInventoryWorkflowTests
{
    private readonly FakeDateTime _clock = new();

    [Fact]
    public async Task SaveAndNext_CreatesDistinctItemsWithoutCopyingBarcodePackagingOrPrices()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var editor = new ItemEditViewModel(new InventoryService(db, _clock));
        await editor.LoadForCreateAsync();
        editor.Code = "FAST-1"; editor.Name = "الصنف الأول"; editor.Barcode = "123456";
        editor.UnitsPerPackage = 100; editor.RequiresPrescription = true;
        editor.SaleUnits.Add(new ItemSaleUnitDto { Name = "شريط", BaseUnitCount = 20 });
        editor.DefaultPurchasePrice = 1000;
        var ready = 0; var closed = 0; editor.NewItemReady += () => ready++; editor.RequestClose += () => closed++;
        await editor.SaveAndAddAnotherAsync();
        editor.SavedSuccessfully.Should().BeTrue(); ready.Should().Be(1); closed.Should().Be(0);
        editor.Code.Should().BeEmpty(); editor.Name.Should().BeEmpty(); editor.Barcode.Should().BeNull();
        editor.SaleUnits.Should().BeEmpty(); editor.UnitsPerPackage.Should().Be(1);
        editor.DefaultPurchasePrice.Should().Be(0); editor.DefaultSalePrice.Should().Be(0);
        editor.RequiresPrescription.Should().BeFalse(); editor.CategoryId.Should().Be(f.CategoryId);
        editor.UnitOfMeasureId.Should().Be(f.UnitOfMeasureId); editor.SuccessMessage.Should().Contain("الصنف الأول");
        editor.Code = "FAST-2"; editor.Name = "الصنف الثاني"; editor.DefaultPurchasePrice = 2000;
        await editor.SaveAsync(); closed.Should().Be(1);
        var items = await db.Items.Where(i => i.Code.StartsWith("FAST-")).OrderBy(i => i.Code).ToListAsync();
        items.Should().HaveCount(2); items[0].Barcode.Should().Be("123456"); items[1].Barcode.Should().BeNull();
        items[0].DefaultSalePrice.Should().Be(1250); items[1].DefaultSalePrice.Should().Be(2500);
        editor.SavedItemId.Should().Be(items[1].Id); editor.SavedItemCode.Should().Be("FAST-2");
    }

    [Fact]
    public async Task SaveAndReceive_RequiresActionPermissionAndProvidesSavedIdentityAfterBusyStateEnds()
    {
        await using var db = TestDb.CreateContext(_clock); await TestDb.SeedBaselineAsync(db);
        var editor = new ItemEditViewModel(new InventoryService(db, _clock));
        await editor.LoadForCreateAsync(); editor.Code = "FIRST-RECEIPT"; editor.Name = "صنف للاستلام";
        editor.DefaultPurchasePrice = 1000; editor.DefaultSalePrice = 1300;
        await editor.SaveAndReceiveAsync(); (await db.Items.CountAsync()).Should().Be(1);
        editor.CanReceiveStock = true;
        var closed = false;
        editor.RequestClose += () => { closed = true; editor.IsBusy.Should().BeFalse(); editor.ReceiveStockAfterSave.Should().BeTrue(); };
        await editor.SaveAndReceiveAsync(); closed.Should().BeTrue();
        editor.SavedSuccessfully.Should().BeTrue(); editor.SavedItemId.Should().NotBeNull();
        var saved = await db.Items.SingleAsync(i => i.Id == editor.SavedItemId);
        saved.Code.Should().Be("FIRST-RECEIPT"); saved.DefaultSalePrice.Should().Be(1300);
    }

    [Fact]
    public async Task FailedSaveAndNext_PreservesEnteredItemAndDoesNotCloseOrAdvance()
    {
        await using var db = TestDb.CreateContext(_clock); await TestDb.SeedBaselineAsync(db);
        var editor = new ItemEditViewModel(new InventoryService(db, _clock));
        await editor.LoadForCreateAsync(); editor.Code = "ITM-001"; editor.Name = "إدخال مكرر";
        var ready = false; editor.NewItemReady += () => ready = true;
        await editor.SaveAndAddAnotherAsync(); ready.Should().BeFalse(); editor.SavedSuccessfully.Should().BeFalse();
        editor.ErrorMessage.Should().NotBeEmpty(); editor.Code.Should().Be("ITM-001"); editor.Name.Should().Be("إدخال مكرر");
        (await db.Items.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReceiveAndNext_KeepsWarehouseUnitAndPricesButRequiresFreshLotAndQuantity()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var item = await db.Items.SingleAsync(); item.UnitsPerPackage = 5; item.DefaultPurchasePrice = 1000;
        await db.SaveChangesAsync();
        var inventory = new InventoryService(db, _clock);
        var editor = new ReceiveBatchViewModel(inventory, new BranchService(db, new PermissiveLicenseService()), new FakeCurrentUserService());
        await editor.InitializeAsync(f.ItemId, f.BranchId);
        editor.ExpiryDate.Should().BeNull();
        editor.BatchNumber = "FAST-A"; editor.Quantity = 2; editor.BonusQuantity = 1;
        editor.ExpiryDate = _clock.UtcNow.AddMonths(6); editor.SalePriceOverride = 1300; editor.SupplierReference = "INV-1";
        editor.ReceiptSummary.Should().Contain("15").And.Contain(2000m.ToString("N2"));
        var ready = 0; var closed = false; editor.NextBatchReady += () => ready++; editor.RequestClose += () => closed = true;
        await editor.SaveAndAddAnotherAsync();
        editor.SavedSuccessfully.Should().BeTrue(); closed.Should().BeFalse(); ready.Should().Be(1);
        editor.BatchNumber.Should().BeEmpty(); editor.Quantity.Should().Be(0); editor.BonusQuantity.Should().Be(0);
        editor.ExpiryDate.Should().BeNull();
        editor.WarehouseId.Should().Be(f.WarehouseId); editor.PurchasePrice.Should().Be(1000); editor.SalePriceOverride.Should().Be(1300);
        editor.SupplierReference.Should().Be("INV-1"); editor.SuccessMessage.Should().Contain("FAST-A");
        await editor.SaveAndAddAnotherAsync(); ready.Should().Be(1); editor.ErrorMessage.Should().NotBeEmpty();
        editor.BatchNumber = "FAST-B"; editor.Quantity = 1;
        await editor.SaveAsync(); closed.Should().BeFalse(); editor.ErrorMessage.Should().Contain("الصلاحية");
        (await db.Batches.CountAsync()).Should().Be(1);
        editor.ExpiryDate = _clock.UtcNow.AddMonths(8);
        await editor.SaveAsync(); closed.Should().BeTrue();
        (await db.Batches.SumAsync(b => b.QuantityOnHand)).Should().Be(20);
        (await db.StockTransactions.CountAsync()).Should().Be(2);
    }
}
