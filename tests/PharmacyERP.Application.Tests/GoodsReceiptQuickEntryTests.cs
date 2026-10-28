using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.ViewModels.Purchasing;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class GoodsReceiptQuickEntryTests
{
    private readonly FakeDateTime _clock = new();
    private GoodsReceiptEditViewModel Editor(ApplicationDbContext db) => new(
        new PurchasingService(db, new InventoryService(db, _clock), new AccountingService(db, _clock), _clock),
        new InventoryService(db, _clock), new BranchService(db, new PermissiveLicenseService()));

    [Fact]
    public async Task AdditionalUnitBarcode_AddsIndependentRowsWithCorrectPurchaseFactorAndNoInventedExpiry()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var item = await db.Items.SingleAsync(); item.Barcode = "BOX-100"; item.BaseUnitBarcode = "PILL-1";
        item.UnitsPerPackage = 100; item.PackageUnitName = "علبة"; item.DefaultPurchasePrice = 10000;
        db.ItemSaleUnits.Add(new ItemSaleUnit { ItemId = f.ItemId, Name = "شريط", BaseUnitCount = 20, Barcode = "STRIP-20" });
        db.Suppliers.Add(new Supplier { Code = "S1", Name = "المذخر", IsActive = true }); await db.SaveChangesAsync();
        var editor = Editor(db); await editor.LoadForCreateAsync(f.BranchId);
        await editor.SelectSupplierAsync((await db.Suppliers.SingleAsync()).Id);
        editor.ReceiptDate = _clock.UtcNow.Date;
        editor.QuickSearchText = "STRIP-20"; editor.AddQuickItem().Should().BeTrue();
        var first = editor.Lines.Single(); first.SelectedPurchaseUnit!.BaseUnitCount.Should().Be(20);
        first.UnitCost.Should().Be(2000); first.SalePrice.Should().Be(2500);
        first.BatchNumber.Should().BeEmpty(); first.ExpiryDate.Should().BeNull();
        editor.QuickSearchText.Should().BeEmpty(); editor.QuickMatches.Should().BeEmpty();
        editor.QuickSearchText = "STRIP-20"; editor.AddQuickItem().Should().BeTrue();
        editor.Lines.Should().HaveCount(2); editor.Lines[1].Should().NotBeSameAs(first);
        first.BatchNumber = "B1"; editor.Lines[1].BatchNumber = "B2";
        await editor.SaveAsync(); editor.SavedSuccessfully.Should().BeFalse(); editor.ErrorMessage.Should().Contain("الصلاحية");
        (await db.GoodsReceiptNotes.CountAsync()).Should().Be(0);
        foreach (var row in editor.Lines) row.ExpiryDate = _clock.UtcNow.AddMonths(6);
        var closeRequested = false;
        editor.RequestClose += () => { closeRequested = true; editor.IsBusy.Should().BeFalse(); };
        await editor.SaveAsync(); editor.SavedSuccessfully.Should().BeTrue(); closeRequested.Should().BeTrue();
        (await db.GoodsReceiptNotes.CountAsync()).Should().Be(1);
        (await db.GoodsReceiptItems.CountAsync()).Should().Be(2);
        (await db.Batches.CountAsync()).Should().Be(0); // Draft remains separate from posting stock.
    }

    [Fact]
    public void Search_IsBoundedDoesNotPickAmbiguousNamesAndRejectsUnsupportedBaseBarcode()
    {
        using var db = TestDb.CreateContext(_clock); var editor = Editor(db);
        for (var i = 1; i <= 20; i++) editor.AvailableItems.Add(new ItemDto { Id = i, Code = $"P{i}", Name = $"Panadol {i}",
            UnitsPerPackage = 5, Barcode = $"BOX{i}", BaseUnitBarcode = $"BASE{i}", DefaultPurchasePrice = 1000 });
        editor.QuickSearchText = "Panadol"; editor.QuickMatches.Should().HaveCount(12);
        editor.AddQuickItem().Should().BeFalse(); editor.Lines.Should().BeEmpty();
        editor.SelectedQuickItem = editor.QuickMatches[2]; editor.AddQuickItem().Should().BeTrue();
        editor.Lines.Should().ContainSingle();
        editor.QuickSearchText = "BASE1"; editor.AddQuickItem().Should().BeFalse();
        editor.QuickEntryMessage.Should().Contain("وحدة المخزون"); editor.Lines.Should().ContainSingle();
        editor.QuickSearchText = "P1"; editor.AddQuickItem().Should().BeTrue();
        editor.Lines.Last().UnitCost.Should().Be(1000); editor.Lines.Last().ItemSaleUnitId.Should().BeNull();
    }

    [Fact]
    public async Task InitializationHeaderChangesAndReload_SerializeQueriesOnTheSharedContext()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        db.Suppliers.Add(new Supplier { Code = "S1", Name = "المذخر", IsActive = true }); await db.SaveChangesAsync();
        var inventory = new InventoryService(db, _clock);
        var concurrency = new QueryConcurrency();
        var editor = new GoodsReceiptEditViewModel(
            DelayedProxy.Wrap<IPurchasingService>(new PurchasingService(db, inventory, new AccountingService(db, _clock), _clock), concurrency),
            DelayedProxy.Wrap<IInventoryService>(inventory, concurrency),
            DelayedProxy.Wrap<IBranchService>(new BranchService(db, new PermissiveLicenseService()), concurrency));
        var supplierId = (await db.Suppliers.SingleAsync()).Id;
        var initialize = editor.LoadForCreateAsync();
        editor.SupplierId = supplierId; editor.BranchId = f.BranchId;
        await Task.WhenAll(initialize, editor.ReloadItemsAsync());
        editor.ErrorMessage.Should().BeEmpty(); concurrency.Maximum.Should().Be(1);
        editor.AvailableItems.Should().ContainSingle(); editor.Warehouses.Should().ContainSingle();
        editor.WarehouseId.Should().Be(f.WarehouseId); editor.IsBusy.Should().BeFalse();
    }

    public sealed class QueryConcurrency
    {
        public int Active; public int Maximum;
    }
    public class DelayedProxy : DispatchProxy
    {
        public object Target = null!; public QueryConcurrency Concurrency = null!;
        public static T Wrap<T>(T target, QueryConcurrency concurrency) where T : class
        {
            var proxy = Create<T, DelayedProxy>(); var interceptor = (DelayedProxy)(object)proxy;
            interceptor.Target = target; interceptor.Concurrency = concurrency; return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                return typeof(DelayedProxy).GetMethod(nameof(InvokeDelayed), BindingFlags.Instance | BindingFlags.NonPublic)!
                    .MakeGenericMethod(method.ReturnType.GenericTypeArguments[0]).Invoke(this, new object?[] { method, args });
            return method.Invoke(Target, args);
        }
        private async Task<T> InvokeDelayed<T>(MethodInfo method, object?[]? args)
        {
            var active = Interlocked.Increment(ref Concurrency.Active); Concurrency.Maximum = Math.Max(Concurrency.Maximum, active);
            try { await Task.Delay(15); return await (Task<T>)method.Invoke(Target, args)!; }
            finally { Interlocked.Decrement(ref Concurrency.Active); }
        }
    }
}
