using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.Domain.Common;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

/// <summary>
/// ViewModel for the Purchase Order editor. Lines are edited directly in a
/// DataGrid (add/remove rows, pick item from a ComboBox column) rather than
/// through a nested dialog, matching how pharmacy staff expect to key in a
/// multi-line order quickly.
/// </summary>
public class PurchaseOrderEditViewModel : PurchasePricingViewModel
{
    private readonly IPurchasingService _purchasingService;
    private readonly IInventoryService _inventoryService;
    private readonly IBranchService _branchService;

    private int? _id;
    private int _supplierId;
    private int _branchId;
    private int _warehouseId;
    private DateTime _orderDate = DateTime.Today;
    private DateTime? _expectedDeliveryDate;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private POLineRow? _selectedLine;

    public PurchaseOrderEditViewModel(IPurchasingService purchasingService, IInventoryService inventoryService, IBranchService branchService)
    {
        _purchasingService = purchasingService;
        _inventoryService = inventoryService;
        _branchService = branchService;

        Suppliers = new ObservableCollection<SupplierDto>();
        Branches = new ObservableCollection<BranchDto>();
        Warehouses = new ObservableCollection<WarehouseDto>();
        AvailableItems = new ObservableCollection<ItemDto>();
        Lines = new ObservableCollection<POLineRow>();

        AddLineCommand = new RelayCommand(() => Lines.Add(new POLineRow { PurchaseType = PurchaseType }));
        RemoveLineCommand = new RelayCommand(() => { if (SelectedLine is not null) Lines.Remove(SelectedLine); }, () => SelectedLine is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    protected override void OnPurchaseTypeChanged()
    {
        foreach (var line in Lines) line.PurchaseType = PurchaseType;
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل أمر الشراء" : "أمر شراء جديد";

    public ObservableCollection<SupplierDto> Suppliers { get; }
    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<WarehouseDto> Warehouses { get; }
    public ObservableCollection<ItemDto> AvailableItems { get; }
    public ObservableCollection<POLineRow> Lines { get; }

    public int SupplierId { get => _supplierId; set => SetProperty(ref _supplierId, value); }

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
    public DateTime OrderDate { get => _orderDate; set => SetProperty(ref _orderDate, value); }
    public DateTime? ExpectedDeliveryDate { get => _expectedDeliveryDate; set => SetProperty(ref _expectedDeliveryDate, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public POLineRow? SelectedLine
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

    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadForCreateAsync()
    {
        _id = null;
        await LoadLookupsAsync();
    }

    public async Task LoadForEditAsync(int purchaseOrderId)
    {
        await LoadLookupsAsync();

        var dto = await _purchasingService.GetPurchaseOrderForEditAsync(purchaseOrderId);
        if (dto is null) return;

        _id = dto.Id;
        SupplierId = dto.SupplierId;
        BranchId = dto.BranchId;
        await LoadWarehousesForBranchAsync();
        WarehouseId = dto.WarehouseId;
        OrderDate = dto.OrderDate;
        ExpectedDeliveryDate = dto.ExpectedDeliveryDate;
        Lines.Clear();
        PurchaseType = dto.PurchaseType;
        Notes = dto.Notes;
        foreach (var line in dto.Lines)
        {
            var item = AvailableItems.FirstOrDefault(i => i.Id == line.ItemId);
            Lines.Add(new POLineRow
            {
                PurchaseType = PurchaseType,
                Id = line.Id,
                PurchaseUnits = item?.PurchaseUnits ?? Array.Empty<PharmacyERP.Application.Features.Inventory.DTOs.PurchaseUnitOption>(),
                ItemSaleUnitId = line.ItemSaleUnitId,
                ItemId = line.ItemId,
                ItemCode = item?.Code ?? string.Empty,
                ItemName = item?.DisplayName ?? string.Empty,
                PurchaseUnitDescription = item?.PackagingDescription ?? string.Empty,
                QuantityOrdered = line.QuantityOrdered,
                UnitCost = line.UnitCost,
                SalePrice = line.SalePrice ?? SalePricePolicy.FromPurchasePrice(line.UnitCost, PurchaseType),
                TaxRatePercent = line.TaxRatePercent
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

        if (BranchId == 0 && Branches.Count > 0) BranchId = Branches.First().Id;
        else await LoadWarehousesForBranchAsync();
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

    /// <summary>Called from the View's code-behind when the item ComboBox selection changes, to auto-fill code/name/default cost for a new line.</summary>
    public void ApplyItemSelection(POLineRow line, int itemId)
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
        if (line.TaxRatePercent == 0) line.TaxRatePercent = item.TaxRatePercent;
    }

    public async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (SupplierId <= 0) { ErrorMessage = "الرجاء اختيار المورد."; return; }
        if (BranchId <= 0 || WarehouseId <= 0) { ErrorMessage = "الرجاء اختيار الفرع والمخزن."; return; }
        if (!Lines.Any()) { ErrorMessage = "يجب إضافة صنف واحد على الأقل."; return; }
        if (Lines.Any(l => l.ItemId <= 0)) { ErrorMessage = "الرجاء اختيار الصنف لكل سطر."; return; }
        if (Lines.Any(l => l.QuantityOrdered <= 0)) { ErrorMessage = "الكمية يجب أن تكون أكبر من صفر لكل سطر."; return; }

        IsBusy = true;
        try
        {
            var dto = new PurchaseOrderUpsertDto
            {
                Id = _id,
                SupplierId = SupplierId,
                BranchId = BranchId,
                WarehouseId = WarehouseId,
                OrderDate = OrderDate,
                ExpectedDeliveryDate = ExpectedDeliveryDate,
                PurchaseType = PurchaseType,
                Notes = Notes,
                Lines = Lines.Select(l => new PurchaseOrderLineUpsertDto
                {
                    Id = l.Id,
                    ItemSaleUnitId = l.ItemSaleUnitId,
                    ItemId = l.ItemId,
                    QuantityOrdered = l.QuantityOrdered,
                    UnitCost = l.UnitCost,
                    SalePrice = l.SalePrice,
                    TaxRatePercent = l.TaxRatePercent
                }).ToList()
            };

            var result = _id.HasValue
                ? await _purchasingService.UpdatePurchaseOrderAsync(dto)
                : await _purchasingService.CreatePurchaseOrderAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ أمر الشراء.";
                return;
            }

            SavedSuccessfully = true;
            RequestClose?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
