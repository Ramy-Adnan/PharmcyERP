using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.ViewModels.Purchasing;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class ReceivingDepthTests
{
    private readonly FakeDateTime _clock = new();
    private static readonly PurchaseUnitOption[] Units = { new(null, "علبة", 5), new(10, "شريط", 1) };
    private GoodsReceiptEditViewModel Editor(ApplicationDbContext db, IInventoryService? inventory = null) => new(
        new PurchasingService(db, new InventoryService(db, _clock), new AccountingService(db, _clock, new PermissiveLicenseService()), _clock, new PermissiveLicenseService()),
        inventory ?? new InventoryService(db, _clock), new BranchService(db, new PermissiveLicenseService()));

    [Theory]
    [InlineData(false, PurchasePricingType.Other)]
    [InlineData(true, PurchasePricingType.Other)]
    [InlineData(false, PurchasePricingType.ByHand)]
    [InlineData(true, PurchasePricingType.ByHand)]
    public void PurchaseUnitSwitch_PreservesManualSalePrice_AndExplicitRecalculationRestoresPolicy(bool receipt, PurchasePricingType type)
    {
        PurchasePriceRowBase row = receipt ? new GRLineRow() : new POLineRow();
        row.PurchaseType = type; row.PurchaseUnits = Units;
        row.UnitCost = 1000; row.SalePrice = 1350;
        row.SelectedPurchaseUnit = Units[1];
        row.UnitCost.Should().Be(200); row.SalePrice.Should().Be(270);
        row.SelectedPurchaseUnit = Units[0];
        row.UnitCost.Should().Be(1000); row.SalePrice.Should().Be(1350);
        row.CalculateSalePrice(); row.SelectedPurchaseUnit = Units[1];
        row.SalePrice.Should().Be(type == PurchasePricingType.ByHand ? 240 : 250);
        row.SalePrice = 290; row.UnitCost = 300;
        row.SalePrice.Should().Be(type == PurchasePricingType.ByHand ? 360 : 375, "editing cost still deliberately recalculates the policy");
    }

    [Fact]
    public void AutomaticSalePrice_UsesTheRoundedNewUnitCostRatherThanConvertingTheOldRoundedSalePrice()
    {
        var row = new GRLineRow { PurchaseType = PurchasePricingType.Other,
            PurchaseUnits = new[] { new PurchaseUnitOption(null, "علبة", 3), new PurchaseUnitOption(10, "حبة", 1) }, UnitCost = 1.13m };
        row.SalePrice.Should().Be(1.41m);
        row.SelectedPurchaseUnit = row.PurchaseUnits[1];
        row.UnitCost.Should().Be(0.38m); row.SalePrice.Should().Be(0.48m);
        var invoiceRow = new PILineRow { PurchaseUnits = Units, UnitCost = 1000, Quantity = 2 };
        invoiceRow.SelectedPurchaseUnit = Units[1]; invoiceRow.UnitCost.Should().Be(200); invoiceRow.LineTotal.Should().Be(400);
    }

    [Fact]
    public async Task ReceiptSummary_TracksPurchaseValueBonusAndUnitConversion_AndFindsTheActualMissingField()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var editor = Editor(db); editor.ReceiptDate = _clock.UtcNow.Date;
        var row = new GRLineRow { PurchaseType = PurchasePricingType.Other, ItemId = f.ItemId, ItemName = "اختبار",
            PurchaseUnits = Units, QuantityReceived = 2, BonusQuantity = 1, UnitCost = 1000, SalePrice = 1350 };
        editor.Lines.Add(row);
        editor.TotalPurchaseValue.Should().Be(2000); editor.TotalStockUnits.Should().Be(15); editor.IncompleteLineCount.Should().Be(1);
        var requestedFields = new List<string>();
        editor.IncompleteLineRequested += (selected, field) => { selected.Should().BeSameAs(row); requestedFields.Add(field); };
        editor.FocusFirstIncompleteLine().Should().BeTrue(); requestedFields.Last().Should().Be(nameof(GRLineRow.BatchNumber));
        row.BatchNumber = "B1";
        editor.FocusFirstIncompleteLine().Should().BeTrue(); requestedFields.Last().Should().Be(nameof(GRLineRow.ExpiryDate));
        row.ExpiryDate = _clock.UtcNow.AddMonths(6);
        editor.IncompleteLineCount.Should().Be(0); editor.FocusFirstIncompleteLine().Should().BeFalse();
        row.QuantityReceived = 3;
        editor.TotalPurchaseValue.Should().Be(3000); editor.TotalStockUnits.Should().Be(20);
        row.SelectedPurchaseUnit = Units[1];
        editor.TotalPurchaseValue.Should().Be(600); editor.TotalStockUnits.Should().Be(4); row.SalePrice.Should().Be(270);
        editor.ReceiptDate = row.ExpiryDate.Value;
        editor.IncompleteLineCount.Should().Be(1); row.EntryIssue.Should().Contain("الصلاحية");
        editor.Lines.Remove(row); editor.TotalPurchaseValue.Should().Be(0); editor.TotalStockUnits.Should().Be(0); editor.IncompleteLineCount.Should().Be(0);
    }

    [Fact]
    public async Task ReceiptQueue_RemainsBusyUntilEveryAlreadyQueuedQueryHasCompleted()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var proxy = DispatchProxy.Create<IInventoryService, PausedItems>();
        var pause = (PausedItems)proxy; pause.Target = new InventoryService(db, _clock);
        var editor = Editor(db, proxy);
        var initialize = editor.LoadForCreateAsync(f.BranchId);
        await pause.FirstEntered.Task;
        var reload = editor.ReloadItemsAsync();
        var busyChanges = new List<bool>();
        editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(editor.IsBusy)) busyChanges.Add(editor.IsBusy); };
        pause.FirstRelease.SetResult(); await initialize; await pause.SecondEntered.Task;
        editor.IsBusy.Should().BeTrue(); busyChanges.Should().NotContain(false, "queued work still owns the shared context");
        pause.SecondRelease.SetResult(); await reload;
        editor.IsBusy.Should().BeFalse(); editor.ErrorMessage.Should().BeEmpty();
    }

    public class PausedItems : DispatchProxy
    {
        public IInventoryService Target = null!;
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == nameof(IInventoryService.GetItemsAsync)
            ? ReadAsync((string?)args![0], (CancellationToken)args[1]!) : method.Invoke(Target, args);
        private async Task<List<ItemDto>> ReadAsync(string? search, CancellationToken cancellationToken)
        {
            if (++_calls == 1) { FirstEntered.SetResult(); await FirstRelease.Task; }
            else if (_calls == 2) { SecondEntered.SetResult(); await SecondRelease.Task; }
            return await Target.GetItemsAsync(search, cancellationToken);
        }
    }
}
