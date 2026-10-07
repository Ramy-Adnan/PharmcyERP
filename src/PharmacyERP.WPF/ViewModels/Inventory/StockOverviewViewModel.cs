using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Inventory;

/// <summary>
/// Read-only reporting screen: current stock levels per item (with a
/// low-stock filter) and a separate list of batches nearing/at expiry, both
/// scoped to a warehouse the user picks from a dropdown of their accessible
/// branches' warehouses.
/// </summary>
public class StockOverviewViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IBranchService _branchService;
    private readonly ICurrentUserService _currentUserService;

    private WarehouseDto? _selectedWarehouse;
    private bool _lowStockOnly;
    private int _expiryWindowDays = 90;
    private bool _isBusy;

    public StockOverviewViewModel(IInventoryService inventoryService, IBranchService branchService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService;
        _branchService = branchService;
        _currentUserService = currentUserService;

        Warehouses = new ObservableCollection<WarehouseDto>();
        StockSummary = new ObservableCollection<ItemStockSummaryDto>();
        ExpiringBatches = new ObservableCollection<ExpiringBatchDto>();

        RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
    }

    public ObservableCollection<WarehouseDto> Warehouses { get; }
    public ObservableCollection<ItemStockSummaryDto> StockSummary { get; }
    public ObservableCollection<ExpiringBatchDto> ExpiringBatches { get; }

    public WarehouseDto? SelectedWarehouse
    {
        get => _selectedWarehouse;
        set { if (SetProperty(ref _selectedWarehouse, value)) _ = LoadDataAsync(); }
    }

    public bool LowStockOnly
    {
        get => _lowStockOnly;
        set { if (SetProperty(ref _lowStockOnly, value)) _ = LoadDataAsync(); }
    }

    public int ExpiryWindowDays
    {
        get => _expiryWindowDays;
        set { if (SetProperty(ref _expiryWindowDays, value)) _ = LoadDataAsync(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }

    public async Task InitializeAsync()
    {
        var branchId = _currentUserService.CurrentBranchId;
        if (branchId is null) return;

        var warehouses = await _branchService.GetWarehousesAsync(branchId.Value);
        Warehouses.Clear();
        foreach (var w in warehouses.Where(w => w.IsActive)) Warehouses.Add(w);

        SelectedWarehouse = Warehouses.FirstOrDefault(w => w.IsDefault) ?? Warehouses.FirstOrDefault();
    }

    private async Task LoadDataAsync()
    {
        if (SelectedWarehouse is null) return;

        IsBusy = true;
        try
        {
            var summary = await _inventoryService.GetStockOverviewAsync(SelectedWarehouse.Id, LowStockOnly);
            StockSummary.Clear();
            foreach (var s in summary) StockSummary.Add(s);

            var expiring = await _inventoryService.GetExpiringBatchesAsync(SelectedWarehouse.Id, ExpiryWindowDays);
            ExpiringBatches.Clear();
            foreach (var e in expiring) ExpiringBatches.Add(e);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
