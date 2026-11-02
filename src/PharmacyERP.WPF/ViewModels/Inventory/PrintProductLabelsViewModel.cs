using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Inventory;

/// <summary>Read-only product label preparation. Prices come from the selected live batch.</summary>
public class PrintProductLabelsViewModel : ViewModelBase
{
    private readonly IInventoryService _inventory;
    private readonly ISalesService _sales;
    private readonly IBranchService _branches;
    private readonly ICurrentUserService _user;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private int _itemId, _copies = 1, _pending;
    private string _code = "", _productName = "", _error = "";
    private bool _busy, _applying, _closing;
    private double _widthMm = 50, _heightMm = 30;
    private WarehouseDto? _warehouse;
    private BatchDto? _batch;
    private ProductLabelUnit? _unit;
    private SaleItemLookupDto? _item;

    public PrintProductLabelsViewModel(IInventoryService inventory, ISalesService sales, IBranchService branches, ICurrentUserService user)
    { _inventory = inventory; _sales = sales; _branches = branches; _user = user; }

    public ObservableCollection<WarehouseDto> Warehouses { get; } = new();
    public ObservableCollection<BatchDto> Batches { get; } = new();
    public ObservableCollection<ProductLabelUnit> Units { get; } = new();
    public string ProductName { get => _productName; private set => SetProperty(ref _productName, value); }
    public string ErrorMessage { get => _error; private set => SetProperty(ref _error, value); }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) NotifyLabel(); } }
    public bool CanAccess => _user.UserId.HasValue && (_user.HasPermission("Inventory.ManageItems") || _user.HasPermission("Inventory.ViewStock"));
    public WarehouseDto? SelectedWarehouse
    {
        get => _warehouse;
        set
        {
            var selected = value is null ? null : Warehouses.FirstOrDefault(w => w.Id == value.Id);
            if (!SetProperty(ref _warehouse, selected)) return;
            if (!_applying && !_closing) _ = RunAsync(() => RefreshCoreAsync(null));
            NotifyLabel();
        }
    }
    public BatchDto? SelectedBatch { get => _batch; set { if (SetProperty(ref _batch, value is null ? null : Batches.FirstOrDefault(b => b.Id == value.Id))) NotifyLabel(); } }
    public ProductLabelUnit? SelectedUnit { get => _unit; set { if (SetProperty(ref _unit, value is null ? null : Units.FirstOrDefault(u => u.Key == value.Key))) NotifyLabel(); } }
    public string Barcode => SelectedUnit?.Barcode ?? "";
    public string UnitName => SelectedUnit?.Name ?? "";
    public decimal SalePrice => SelectedBatch is { } batch && SelectedUnit is { } unit
        ? ProductLabelSelection.PriceForBatch(_item, batch.Id, unit.Factor) ?? 0 : 0;
    public string PriceNotice => SelectedUnit is { } unit && _item?.BatchPrices.FirstOrDefault() is { } first
        && first.BatchId != SelectedBatch?.Id && first.UnitPrices.GetValueOrDefault(unit.Factor) != SalePrice
        ? "سعر الدفعة المختارة يختلف عن أول دفعة للصرف. الباركود مشترك للصنف والوحدة؛ راجع سعر السلة عند البيع من دفعات مختلفة."
        : "الباركود يعرّف الصنف والوحدة؛ السعر المطبوع لا يغيّر أسعار البيع في النظام.";
    public int Copies { get => _copies; set { if (SetProperty(ref _copies, value)) NotifyLabel(); } }
    public double WidthMm { get => _widthMm; set { if (SetProperty(ref _widthMm, value)) NotifyLabel(); } }
    public double HeightMm { get => _heightMm; set { if (SetProperty(ref _heightMm, value)) NotifyLabel(); } }
    public bool CanPrint => !_closing && !IsBusy && InputError() is null;
    public string ValidationMessage => InputError() ?? string.Empty;
    public ProductLabelRequest? PreviewLabel => InputError() is null ? Snapshot() : null;

    private string? InputError()
    {
        if (!CanAccess) return "طباعة الملصقات تحتاج صلاحية إدارة الأصناف أو عرض المخزون.";
        if (SelectedWarehouse is null || !SelectedWarehouse.IsActive || SelectedWarehouse.BranchId != _user.CurrentBranchId)
            return "اختر مخزناً نشطاً من الفرع الحالي.";
        if (SelectedBatch is null || SelectedBatch.ItemId != _itemId || SelectedBatch.WarehouseId != SelectedWarehouse.Id ||
            SelectedBatch.IsExpired || SelectedBatch.QuantityOnHand <= 0 || !Batches.Contains(SelectedBatch))
            return "اختر دفعة صالحة ذات رصيد.";
        var barcodeError = ProductLabelSelection.BarcodeError(SelectedUnit, Units.ToList());
        if (barcodeError is not null) return barcodeError;
        if (SalePrice <= 0) return "سعر البيع غير محدد لهذه الدفعة والوحدة. حدّد سعر البيع أولاً.";
        return ProductLabelSelection.LayoutError(Copies, WidthMm, HeightMm);
    }

    private void NotifyLabel()
    {
        foreach (var property in new[] { nameof(Barcode), nameof(UnitName), nameof(SalePrice), nameof(PriceNotice), nameof(CanPrint), nameof(ValidationMessage), nameof(PreviewLabel) }) OnPropertyChanged(property);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public Task InitializeAsync(int itemId, string code, string itemName, int? batchId = null) => RunAsync(async () =>
    {
        _itemId = itemId; _code = code; ProductName = itemName;
        if (!CanAccess) { ErrorMessage = "طباعة الملصقات تحتاج صلاحية إدارة الأصناف أو عرض المخزون."; return; }
        if (_user.CurrentBranchId is not int branchId) { ErrorMessage = "تعذر تحديد الفرع الحالي."; return; }
        var warehouses = await _branches.GetWarehousesAsync(branchId);
        _applying = true;
        try
        {
            Warehouses.Clear(); foreach (var warehouse in warehouses.Where(w => w.IsActive && w.BranchId == branchId)) Warehouses.Add(warehouse);
            SelectedWarehouse = Warehouses.FirstOrDefault(w => w.IsDefault) ?? Warehouses.FirstOrDefault();
        }
        finally { _applying = false; }
        await RefreshCoreAsync(batchId);
    });

    public Task RefreshAsync() => RunAsync(() => RefreshCoreAsync(SelectedBatch?.Id));

    private async Task RefreshCoreAsync(int? batchId)
    {
        var warehouseId = SelectedWarehouse?.Id;
        if (!CanAccess || !warehouseId.HasValue) { ClearSelection(); return; }
        var batchList = await _inventory.GetBatchesForItemAsync(_itemId, warehouseId.Value);
        var items = await _sales.SearchSaleItemsAsync(_code, warehouseId.Value);
        if (warehouseId != SelectedWarehouse?.Id || _closing) return;
        _item = items.SingleOrDefault(i => i.ItemId == _itemId);
        var unitKey = SelectedUnit?.Key;
        Batches.Clear(); Units.Clear();
        if (_item is null) { SelectedBatch = null; SelectedUnit = null; ErrorMessage = "الصنف غير متاح أو غير نشط."; return; }
        ProductName = _item.DisplayName;
        foreach (var batch in batchList.Where(b => !b.IsExpired && b.QuantityOnHand > 0 && _item.BatchPrices.Any(p => p.BatchId == b.Id))
            .OrderBy(b => b.ExpiryDate).ThenBy(b => b.ReceivedAtUtc).ThenBy(b => b.Id)) Batches.Add(batch);
        foreach (var unit in ProductLabelSelection.UnitsFor(_item)) Units.Add(unit);
        SelectedBatch = Batches.FirstOrDefault(b => b.Id == batchId) ?? (batchId.HasValue ? null : Batches.FirstOrDefault());
        SelectedUnit = Units.FirstOrDefault(u => u.Key == unitKey) ?? Units.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Barcode)) ?? Units.FirstOrDefault();
        NotifyLabel();
    }

    private void ClearSelection()
    { _item = null; Batches.Clear(); Units.Clear(); SelectedBatch = null; SelectedUnit = null; NotifyLabel(); }

    public async Task<ProductLabelRequest?> PreparePrintAsync()
    {
        ProductLabelRequest? prepared = null;
        if (IsBusy || _closing) return null;
        await RunAsync(async () =>
        {
            if (InputError() is { } error) { ErrorMessage = error; return; }
            var before = Snapshot();
            var currentWarehouses = await _branches.GetWarehousesAsync(_user.CurrentBranchId!.Value);
            if (!currentWarehouses.Any(w => w.Id == SelectedWarehouse!.Id && w.IsActive && w.BranchId == _user.CurrentBranchId))
            { ClearSelection(); ErrorMessage = "المخزن لم يعد نشطاً أو متاحاً للفرع الحالي. أغلق النافذة وحدّث قائمة المخازن."; return; }
            await RefreshCoreAsync(SelectedBatch!.Id);
            if (InputError() is { } refreshedError) { ErrorMessage = refreshedError; return; }
            var current = Snapshot();
            if (before.Name != current.Name || before.Barcode != current.Barcode || before.UnitName != current.UnitName || before.SalePrice != current.SalePrice)
            { ErrorMessage = "تغيّرت بيانات الصنف أو سعر الدفعة. تم تحديث المعاينة؛ راجعها واضغط طباعة مجدداً."; return; }
            prepared = current;
        });
        return prepared;
    }

    private ProductLabelRequest Snapshot() => new(ProductName, Barcode, UnitName, SalePrice, Copies, WidthMm, HeightMm);

    private async Task RunAsync(Func<Task> action)
    {
        if (_closing) return;
        _pending++;
        IsBusy = true;
        await _operations.WaitAsync();
        try
        {
            if (_closing) return;
            ErrorMessage = string.Empty;
            await action();
        }
        catch (Exception ex) { ErrorMessage = "تعذر تجهيز الملصقات: " + ex.Message; }
        finally { _pending--; IsBusy = _pending > 0; _operations.Release(); }
    }

    public async Task PrepareToCloseAsync()
    { _closing = true; await _operations.WaitAsync(); _operations.Release(); }
}
