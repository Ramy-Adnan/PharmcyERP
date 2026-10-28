using PharmacyERP.Domain.Enums;
using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Domain.Common;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Inventory;

/// <summary>ViewModel for the "Receive Batch" dialog — records a newly-purchased lot of an Item into a specific Warehouse.</summary>
public class ReceiveBatchViewModel : PurchasePricingViewModel
{
    private readonly IInventoryService _inventoryService;
    private readonly IBranchService _branchService;
    private readonly ICurrentUserService _currentUserService;

    private int _itemId;
    private int _bonusQuantity;
    private int? _itemSaleUnitId;
    private IReadOnlyList<PurchaseUnitOption> _purchaseUnits = Array.Empty<PurchaseUnitOption>();
    private int _warehouseId;
    private string _batchNumber = string.Empty;
    private DateTime? _manufactureDate;
    private DateTime? _expiryDate;
    private int _quantity;
    private decimal _purchasePrice;
    private decimal? _salePriceOverride;
    private string? _supplierReference;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private string _successMessage = string.Empty;
    private string _itemName = string.Empty;

    public ReceiveBatchViewModel(IInventoryService inventoryService, IBranchService branchService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService;
        _branchService = branchService;
        _currentUserService = currentUserService;

        Warehouses = new ObservableCollection<WarehouseDto>();
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CalculateSalePriceCommand = new RelayCommand(() => SalePriceOverride = SalePricePolicy.FromPurchasePrice(PurchasePrice, PurchaseType));
    }

    protected override void OnPurchaseTypeChanged() => SalePriceOverride = SalePricePolicy.FromPurchasePrice(PurchasePrice, PurchaseType);

    public ObservableCollection<WarehouseDto> Warehouses { get; }

    public IReadOnlyList<PurchaseUnitOption> PurchaseUnits { get => _purchaseUnits; private set { if (SetProperty(ref _purchaseUnits, value)) OnPropertyChanged(nameof(SelectedPurchaseUnit)); OnPropertyChanged(nameof(PackagingDescription)); OnPropertyChanged(nameof(ReceiptSummary)); } }
    public PurchaseUnitOption? SelectedPurchaseUnit { get => PurchaseUnits.FirstOrDefault(u => u.Id == ItemSaleUnitId); set { if (value is not null) ItemSaleUnitId = value.Id; } }
    public int BonusQuantity { get => _bonusQuantity; set { if (SetProperty(ref _bonusQuantity, value)) OnPropertyChanged(nameof(ReceiptSummary)); } }
    public int? ItemSaleUnitId
    {
        get => _itemSaleUnitId;
        set
        {
            var old = PurchaseUnits.FirstOrDefault(u => u.Id == _itemSaleUnitId)?.BaseUnitCount ?? 1;
            if (!SetProperty(ref _itemSaleUnitId, value)) return;
            PurchasePrice = Math.Round(PurchasePrice / old * (PurchaseUnits.FirstOrDefault(u => u.Id == value)?.BaseUnitCount ?? 1), 2, MidpointRounding.AwayFromZero);
            OnPropertyChanged(nameof(SelectedPurchaseUnit)); OnPropertyChanged(nameof(PackagingDescription)); OnPropertyChanged(nameof(ReceiptSummary));
        }
    }
    public string PackagingDescription => SelectedPurchaseUnit is { } unit ? $"الكمية والأسعار لوحدة الاستلام: {unit.DisplayName}." : string.Empty;
    public int WarehouseId { get => _warehouseId; set => SetProperty(ref _warehouseId, value); }
    public string BatchNumber { get => _batchNumber; set => SetProperty(ref _batchNumber, value); }
    public DateTime? ManufactureDate { get => _manufactureDate; set => SetProperty(ref _manufactureDate, value); }
    public DateTime? ExpiryDate { get => _expiryDate; set => SetProperty(ref _expiryDate, value); }
    public int Quantity { get => _quantity; set { if (SetProperty(ref _quantity, value)) OnPropertyChanged(nameof(ReceiptSummary)); } }
    public decimal PurchasePrice
    {
        get => _purchasePrice;
        set
        {
            if (SetProperty(ref _purchasePrice, value))
                SalePriceOverride = SalePricePolicy.FromPurchasePrice(value, PurchaseType);
            OnPropertyChanged(nameof(ReceiptSummary));
        }
    }
    public decimal? SalePriceOverride { get => _salePriceOverride; set => SetProperty(ref _salePriceOverride, value); }
    public string? SupplierReference { get => _supplierReference; set => SetProperty(ref _supplierReference, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public string ItemName { get => _itemName; private set => SetProperty(ref _itemName, value); }
    public string SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }
    public string ReceiptSummary => $"إجمالي الشراء: {Quantity * PurchasePrice:N2} د.ع — يضاف للمخزن {((long)Quantity + BonusQuantity) * (SelectedPurchaseUnit?.BaseUnitCount ?? 1):N0} من أصغر وحدة، شامل البونص.";

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CalculateSalePriceCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;
    public event Action? NextBatchReady;

    public async Task InitializeAsync(int itemId, int defaultBranchId)
    {
        if (IsBusy) return;
        _itemId = itemId;
        BatchNumber = string.Empty; Quantity = BonusQuantity = 0; ManufactureDate = ExpiryDate = null; SupplierReference = null;
        _itemSaleUnitId = null;
        SavedSuccessfully = false; SuccessMessage = ErrorMessage = string.Empty;
        var item = await _inventoryService.GetItemForEditAsync(itemId);
        OnPropertyChanged(nameof(PackagingDescription));
        if (item is not null)
        {
            ItemName = item.Name;
            var names = await _inventoryService.GetUnitsAsync();
            PurchaseUnits = new[] { new PurchaseUnitOption(null, item.UnitsPerPackage > 1 ? item.PackageUnitName : names.FirstOrDefault(u => u.Id == item.UnitOfMeasureId)?.Name ?? "وحدة", item.UnitsPerPackage) }
                .Concat(item.SaleUnits.Where(u => u.IsActive).Select(u => new PurchaseUnitOption(u.Id, u.Name, u.BaseUnitCount))).ToList();
        }
        PurchaseType = item?.PurchaseType ?? PurchasePricingType.Other;
        PurchasePrice = item?.DefaultPurchasePrice ?? 0;
        SalePriceOverride = SalePricePolicy.FromPurchasePrice(PurchasePrice, PurchaseType);

        var warehouses = await _branchService.GetWarehousesAsync(defaultBranchId);
        Warehouses.Clear();
        foreach (var w in warehouses.Where(w => w.IsActive)) Warehouses.Add(w);

        WarehouseId = Warehouses.FirstOrDefault(w => w.IsDefault)?.Id ?? Warehouses.FirstOrDefault()?.Id ?? 0;
    }

    public async Task SaveAsync()
    {
        if (await SaveCoreAsync()) RequestClose?.Invoke();
    }

    public async Task SaveAndAddAnotherAsync()
    {
        if (!await SaveCoreAsync()) return;
        SuccessMessage = $"تم استلام الدفعة «{BatchNumber}» بنجاح. أدخل رقم وكمية وصلاحية الدفعة التالية لنفس الصنف.";
        BatchNumber = string.Empty; Quantity = BonusQuantity = 0; ManufactureDate = ExpiryDate = null;
        NextBatchReady?.Invoke();
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (IsBusy) return false;
        ErrorMessage = string.Empty;
        if (!ExpiryDate.HasValue)
        { ErrorMessage = "أدخل تاريخ الصلاحية الفعلي المكتوب على الدفعة؛ لا يُفترض تاريخ تلقائيًا."; return false; }
        IsBusy = true;
        try
        {
            var dto = new ReceiveBatchDto
            {
                ItemId = _itemId,
                ItemSaleUnitId = ItemSaleUnitId,
                BonusQuantity = BonusQuantity,
                WarehouseId = WarehouseId,
                BatchNumber = BatchNumber,
                ManufactureDate = ManufactureDate,
                ExpiryDate = ExpiryDate.Value,
                Quantity = Quantity,
                PurchaseType = PurchaseType,
                PurchasePrice = PurchasePrice,
                SalePriceOverride = SalePriceOverride,
                SupplierReference = SupplierReference
            };

            var result = await _inventoryService.ReceiveBatchAsync(dto, _currentUserService.UserId);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر استلام الدفعة.";
                return false;
            }

            SavedSuccessfully = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"تعذر استلام الدفعة: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
