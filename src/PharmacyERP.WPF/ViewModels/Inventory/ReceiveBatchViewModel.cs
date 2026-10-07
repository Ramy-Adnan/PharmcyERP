using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Inventory;

/// <summary>ViewModel for the "Receive Batch" dialog — records a newly-purchased lot of an Item into a specific Warehouse.</summary>
public class ReceiveBatchViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IBranchService _branchService;
    private readonly ICurrentUserService _currentUserService;

    private int _itemId;
    private int _warehouseId;
    private string _batchNumber = string.Empty;
    private DateTime? _manufactureDate;
    private DateTime _expiryDate = DateTime.Today.AddYears(1);
    private int _quantity;
    private decimal _purchasePrice;
    private decimal? _salePriceOverride;
    private string? _supplierReference;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public ReceiveBatchViewModel(IInventoryService inventoryService, IBranchService branchService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService;
        _branchService = branchService;
        _currentUserService = currentUserService;

        Warehouses = new ObservableCollection<WarehouseDto>();
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public ObservableCollection<WarehouseDto> Warehouses { get; }

    public int WarehouseId { get => _warehouseId; set => SetProperty(ref _warehouseId, value); }
    public string BatchNumber { get => _batchNumber; set => SetProperty(ref _batchNumber, value); }
    public DateTime? ManufactureDate { get => _manufactureDate; set => SetProperty(ref _manufactureDate, value); }
    public DateTime ExpiryDate { get => _expiryDate; set => SetProperty(ref _expiryDate, value); }
    public int Quantity { get => _quantity; set => SetProperty(ref _quantity, value); }
    public decimal PurchasePrice { get => _purchasePrice; set => SetProperty(ref _purchasePrice, value); }
    public decimal? SalePriceOverride { get => _salePriceOverride; set => SetProperty(ref _salePriceOverride, value); }
    public string? SupplierReference { get => _supplierReference; set => SetProperty(ref _supplierReference, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task InitializeAsync(int itemId, int defaultBranchId)
    {
        _itemId = itemId;

        var warehouses = await _branchService.GetWarehousesAsync(defaultBranchId);
        Warehouses.Clear();
        foreach (var w in warehouses.Where(w => w.IsActive)) Warehouses.Add(w);

        WarehouseId = Warehouses.FirstOrDefault(w => w.IsDefault)?.Id ?? Warehouses.FirstOrDefault()?.Id ?? 0;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new ReceiveBatchDto
            {
                ItemId = _itemId,
                WarehouseId = WarehouseId,
                BatchNumber = BatchNumber,
                ManufactureDate = ManufactureDate,
                ExpiryDate = ExpiryDate,
                Quantity = Quantity,
                PurchasePrice = PurchasePrice,
                SalePriceOverride = SalePriceOverride,
                SupplierReference = SupplierReference
            };

            var result = await _inventoryService.ReceiveBatchAsync(dto, _currentUserService.UserId);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر استلام الدفعة.";
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
