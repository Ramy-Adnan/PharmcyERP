using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.Domain.Common;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

/// <summary>
/// ViewModel for the Goods Receipt editor. Supports two flows: picking an
/// open Purchase Order to pre-fill outstanding lines (quantities/costs come
/// from what was ordered), or a fully manual receipt for stock arriving
/// without a formal PO. Either way the note is saved as Draft first and only
/// PostGoodsReceiptAsync (triggered from the list screen) commits it to
/// Inventory — this screen never touches stock directly.
/// </summary>
public class GoodsReceiptEditViewModel : PurchasePricingViewModel
{
    private readonly IPurchasingService _purchasingService;
    private readonly IInventoryService _inventoryService;
    private readonly IBranchService _branchService;

    private int? _id;
    private int _supplierId;
    private int? _purchaseOrderId;
    private int _branchId;
    private int _warehouseId;
    private DateTime _receiptDate = DateTime.Today;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private GRLineRow? _selectedLine;

    public GoodsReceiptEditViewModel(IPurchasingService purchasingService, IInventoryService inventoryService, IBranchService branchService)
    {
        _purchasingService = purchasingService;
        _inventoryService = inventoryService;
        _branchService = branchService;

        Suppliers = new ObservableCollection<SupplierDto>();
        OpenPurchaseOrders = new ObservableCollection<PurchaseOrderDto>();
        Branches = new ObservableCollection<BranchDto>();
        Warehouses = new ObservableCollection<WarehouseDto>();
        AvailableItems = new ObservableCollection<ItemDto>();
        Lines = new ObservableCollection<GRLineRow>();

        LoadFromPurchaseOrderCommand = new AsyncRelayCommand(LoadFromPurchaseOrderAsync, () => PurchaseOrderId.HasValue);
        AddLineCommand = new RelayCommand(() => Lines.Add(new GRLineRow { PurchaseType = PurchaseType }));
        RemoveLineCommand = new RelayCommand(() => { if (SelectedLine is not null) Lines.Remove(SelectedLine); }, () => SelectedLine is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    protected override void OnPurchaseTypeChanged()
    {
        foreach (var line in Lines) line.PurchaseType = PurchaseType;
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل سند استلام" : "سند استلام بضاعة جديد";

    public ObservableCollection<SupplierDto> Suppliers { get; }
    public ObservableCollection<PurchaseOrderDto> OpenPurchaseOrders { get; }
    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<WarehouseDto> Warehouses { get; }
    public ObservableCollection<ItemDto> AvailableItems { get; }
    public ObservableCollection<GRLineRow> Lines { get; }

    public int SupplierId
    {
        get => _supplierId;
        set
        {
            if (SetProperty(ref _supplierId, value))
                _ = LoadOpenPurchaseOrdersForSupplierAsync();
        }
    }

    public int? PurchaseOrderId
    {
        get => _purchaseOrderId;
        set
        {
            if (SetProperty(ref _purchaseOrderId, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public int BranchId
    {
        get => _branchId;
        set
        {
            if (SetProperty(ref _branchId, value))
                _ = LoadWarehousesForBranchAsync();
        }
    }

    public int WarehouseId { get => _warehouseId; set => SetProperty(ref _warehouseId, value); }
    public DateTime ReceiptDate { get => _receiptDate; set => SetProperty(ref _receiptDate, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public GRLineRow? SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (SetProperty(ref _selectedLine, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand LoadFromPurchaseOrderCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public int? SavedReceiptId => _id;
    public event Action? RequestClose;

    public async Task LoadForCreateAsync(int? defaultBranchId = null)
    {
        _id = null;
        if (defaultBranchId.HasValue) SetProperty(ref _branchId, defaultBranchId.Value, nameof(BranchId));
        await LoadLookupsAsync();
    }

    public async Task LoadForEditAsync(int goodsReceiptNoteId)
    {
        await LoadLookupsAsync();

        var dto = await _purchasingService.GetGoodsReceiptForEditAsync(goodsReceiptNoteId);
        if (dto is null) return;

        _id = dto.Id;
        SetProperty(ref _supplierId, dto.SupplierId, nameof(SupplierId));
        await LoadOpenPurchaseOrdersForSupplierAsync();
        PurchaseOrderId = dto.PurchaseOrderId;
        SetProperty(ref _branchId, dto.BranchId, nameof(BranchId));
        await LoadWarehousesForBranchAsync();
        WarehouseId = dto.WarehouseId;
        ReceiptDate = dto.ReceiptDate;
        Lines.Clear();
        PurchaseType = dto.PurchaseType;
        Notes = dto.Notes;
        foreach (var line in dto.Lines)
        {
            var item = AvailableItems.FirstOrDefault(i => i.Id == line.ItemId);
            Lines.Add(new GRLineRow
            {
                PurchaseType = PurchaseType,
                Id = line.Id,
                PurchaseOrderItemId = line.PurchaseOrderItemId,
                PurchaseUnits = AvailableItems.FirstOrDefault(i => i.Id == line.ItemId)?.PurchaseUnits ?? Array.Empty<PharmacyERP.Application.Features.Inventory.DTOs.PurchaseUnitOption>(),
                ItemSaleUnitId = line.ItemSaleUnitId,
                ItemId = line.ItemId,
                ItemCode = item?.Code ?? string.Empty,
                ItemName = item?.DisplayName ?? string.Empty,
                PurchaseUnitDescription = item?.PackagingDescription ?? string.Empty,
                BatchNumber = line.BatchNumber,
                ManufactureDate = line.ManufactureDate,
                ExpiryDate = line.ExpiryDate,
                BonusQuantity = line.BonusQuantity,
                QuantityReceived = line.QuantityReceived,
                UnitCost = line.UnitCost,
                SalePrice = line.SalePrice ?? SalePricePolicy.FromPurchasePrice(line.UnitCost, PurchaseType)
            });
        }

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task LoadLookupsAsync()
    {
        var suppliers = await _purchasingService.GetSuppliersAsync();
        Suppliers.Clear();
        foreach (var s in suppliers.Where(s => s.IsActive)) Suppliers.Add(s);

        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);

        var items = await _inventoryService.GetItemsAsync();
        AvailableItems.Clear();
        foreach (var i in items.Where(i => i.IsActive)) AvailableItems.Add(i);

        if (BranchId == 0 && Branches.Count > 0) SetProperty(ref _branchId, Branches.First().Id, nameof(BranchId));
        await LoadWarehousesForBranchAsync();
    }

    public async Task SelectSupplierAsync(int supplierId)
    {
        var suppliers = await _purchasingService.GetSuppliersAsync();
        Suppliers.Clear();
        foreach (var supplier in suppliers.Where(s => s.IsActive)) Suppliers.Add(supplier);
        SetProperty(ref _supplierId, supplierId, nameof(SupplierId));
        await LoadOpenPurchaseOrdersForSupplierAsync();
    }

    public async Task ReloadItemsAsync()
    {
        var items = await _inventoryService.GetItemsAsync();
        AvailableItems.Clear();
        foreach (var item in items.Where(i => i.IsActive)) AvailableItems.Add(item);
    }

    private async Task LoadOpenPurchaseOrdersForSupplierAsync()
    {
        OpenPurchaseOrders.Clear();
        if (SupplierId <= 0) return;

        var allOrders = await _purchasingService.GetPurchaseOrdersAsync();
        foreach (var o in allOrders.Where(o => o.SupplierId == SupplierId &&
                     o.Status is PurchaseOrderStatus.Submitted or PurchaseOrderStatus.PartiallyReceived))
        {
            OpenPurchaseOrders.Add(o);
        }
    }

    private async Task LoadWarehousesForBranchAsync()
    {
        Warehouses.Clear();
        if (BranchId == 0) return;

        var warehouses = await _branchService.GetWarehousesAsync(BranchId);
        foreach (var w in warehouses.Where(w => w.IsActive)) Warehouses.Add(w);

        if (WarehouseId == 0 || Warehouses.All(w => w.Id != WarehouseId))
            WarehouseId = Warehouses.FirstOrDefault()?.Id ?? 0;
    }

    /// <summary>Pulls the selected PO's branch/warehouse and pre-fills one grid row per outstanding line.</summary>
    public async Task LoadFromPurchaseOrderAsync()
    {
        if (!PurchaseOrderId.HasValue) return;

        var poDetails = await _purchasingService.GetPurchaseOrderForEditAsync(PurchaseOrderId.Value);
        if (poDetails is not null)
        {
            Lines.Clear();
            PurchaseType = poDetails.PurchaseType;
            SetProperty(ref _branchId, poDetails.BranchId, nameof(BranchId));
            await LoadWarehousesForBranchAsync();
            WarehouseId = poDetails.WarehouseId;
        }

        var outstandingLines = await _purchasingService.GetOutstandingLinesForReceiptAsync(PurchaseOrderId.Value);

        Lines.Clear();
        foreach (var line in outstandingLines)
        {
            Lines.Add(new GRLineRow
            {
                PurchaseType = PurchaseType,
                PurchaseOrderItemId = line.Id,
                PurchaseUnits = AvailableItems.FirstOrDefault(i => i.Id == line.ItemId)?.PurchaseUnits ?? Array.Empty<PharmacyERP.Application.Features.Inventory.DTOs.PurchaseUnitOption>(),
                ItemSaleUnitId = line.ItemSaleUnitId,
                ItemId = line.ItemId,
                ItemCode = line.ItemCode,
                ItemName = AvailableItems.FirstOrDefault(i => i.Id == line.ItemId)?.DisplayName ?? line.ItemName,
                PurchaseUnitDescription = AvailableItems.FirstOrDefault(i => i.Id == line.ItemId)?.PackagingDescription ?? string.Empty,
                QuantityReceived = line.QuantityOutstanding,
                UnitCost = line.UnitCost,
                SalePrice = line.SalePrice,
                ExpiryDate = DateTime.Today.AddYears(1)
            });
        }
    }

    /// <summary>Called from the View's code-behind when a line's item ComboBox selection changes (manual lines only).</summary>
    public void ApplyItemSelection(GRLineRow line, int itemId)
    {
        var item = AvailableItems.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;

        // SelectionChanged also fires when a saved row is displayed. Preserve its chosen unit and price.
        if (line.ItemCode == item.Code) return;
        line.UnitCost = 0;
        line.PurchaseUnits = item.PurchaseUnits;
        line.ItemSaleUnitId = null;
        line.ItemId = item.Id;
        line.ItemCode = item.Code;
        line.ItemName = item.DisplayName;
        line.PurchaseUnitDescription = item.PackagingDescription;
        if (line.UnitCost == 0) line.UnitCost = item.DefaultPurchasePrice;
    }

    public async Task SaveAsync()
    {
        SavedSuccessfully = false;
        ErrorMessage = string.Empty;

        if (SupplierId <= 0) { ErrorMessage = "الرجاء اختيار المورد."; return; }
        if (BranchId <= 0 || WarehouseId <= 0) { ErrorMessage = "الرجاء اختيار الفرع والمخزن."; return; }
        if (!Lines.Any()) { ErrorMessage = "يجب إضافة صنف واحد على الأقل."; return; }
        if (Lines.Any(l => l.ItemId <= 0)) { ErrorMessage = "الرجاء اختيار الصنف لكل سطر."; return; }
        if (Lines.Any(l => string.IsNullOrWhiteSpace(l.BatchNumber))) { ErrorMessage = "رقم الدفعة مطلوب لكل سطر."; return; }
        if (Lines.Any(l => l.QuantityReceived < 0 || l.BonusQuantity < 0 || (long)l.QuantityReceived + l.BonusQuantity <= 0)) { ErrorMessage = "الكمية يجب أن تكون أكبر من صفر لكل سطر."; return; }
        if (Lines.Any(l => l.ExpiryDate.Date <= ReceiptDate.Date)) { ErrorMessage = "تاريخ انتهاء الصلاحية يجب أن يكون بعد تاريخ الاستلام."; return; }

        IsBusy = true;
        try
        {
            var dto = new GoodsReceiptUpsertDto
            {
                Id = _id,
                PurchaseOrderId = PurchaseOrderId,
                SupplierId = SupplierId,
                BranchId = BranchId,
                WarehouseId = WarehouseId,
                ReceiptDate = ReceiptDate,
                PurchaseType = PurchaseType,
                Notes = Notes,
                Lines = Lines.Select(l => new GoodsReceiptLineUpsertDto
                {
                    Id = l.Id,
                    PurchaseOrderItemId = l.PurchaseOrderItemId,
                    ItemSaleUnitId = l.ItemSaleUnitId,
                    ItemId = l.ItemId,
                    BatchNumber = l.BatchNumber,
                    ManufactureDate = l.ManufactureDate,
                    ExpiryDate = l.ExpiryDate,
                    BonusQuantity = l.BonusQuantity,
                    QuantityReceived = l.QuantityReceived,
                    UnitCost = l.UnitCost,
                    SalePrice = l.SalePrice
                }).ToList()
            };

            var result = _id.HasValue
                ? await _purchasingService.UpdateGoodsReceiptAsync(dto)
                : await _purchasingService.CreateGoodsReceiptAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ سند الاستلام.";
                return;
            }

            _id = result.Value!.Id;
            OnPropertyChanged(nameof(SavedReceiptId));
            SavedSuccessfully = true;
            RequestClose?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
