using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Inventory;

namespace PharmacyERP.WPF.ViewModels.Inventory;

/// <summary>
/// ViewModel for the "Manage Batches" dialog opened from the Items screen:
/// lists every batch of one Item across the branches/warehouses the current
/// user can access, with drill-down transaction history for the selected
/// batch, plus actions to receive new stock or manually adjust a batch.
/// </summary>
public class ItemBatchesViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IBranchService _branchService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private int _itemId;
    private string _itemName = string.Empty;
    private BatchDto? _selectedBatch;
    private bool _isBusy;

    public ItemBatchesViewModel(IInventoryService inventoryService, IBranchService branchService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService;
        _branchService = branchService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Batches = new ObservableCollection<BatchDto>();
        TransactionHistory = new ObservableCollection<StockTransactionDto>();

        RefreshCommand = new AsyncRelayCommand(LoadBatchesAsync);
        ReceiveStockCommand = new AsyncRelayCommand(ReceiveStockAsync, () => CanReceiveStock);
        AdjustStockCommand = new AsyncRelayCommand(AdjustStockAsync, () => CanAdjustStock && SelectedBatch is not null);
    }

    public bool CanReceiveStock => _currentUserService.HasPermission("Inventory.ReceiveStock");
    public bool CanAdjustStock => _currentUserService.HasPermission("Inventory.AdjustStock");

    public string ItemName { get => _itemName; private set => SetProperty(ref _itemName, value); }

    public ObservableCollection<BatchDto> Batches { get; }
    public ObservableCollection<StockTransactionDto> TransactionHistory { get; }

    public BatchDto? SelectedBatch
    {
        get => _selectedBatch;
        set
        {
            if (SetProperty(ref _selectedBatch, value))
            {
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                _ = LoadHistoryAsync();
            }
        }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ReceiveStockCommand { get; }
    public AsyncRelayCommand AdjustStockCommand { get; }

    public async Task InitializeAsync(int itemId, string itemName)
    {
        _itemId = itemId;
        ItemName = itemName;
        await LoadBatchesAsync();
    }

    private async Task LoadBatchesAsync()
    {
        IsBusy = true;
        try
        {
            var batches = await _inventoryService.GetBatchesForItemAsync(_itemId);
            Batches.Clear();
            foreach (var b in batches) Batches.Add(b);
            TransactionHistory.Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHistoryAsync()
    {
        TransactionHistory.Clear();
        if (SelectedBatch is null) return;

        var history = await _inventoryService.GetTransactionHistoryAsync(SelectedBatch.Id);
        foreach (var h in history) TransactionHistory.Add(h);
    }

    private async Task ReceiveStockAsync()
    {
        var branchId = _currentUserService.CurrentBranchId;
        if (branchId is null)
        {
            _dialogService.ShowError("تعذر تحديد الفرع الحالي.");
            return;
        }

        var window = _dialogService.CreateDialog<ReceiveBatchDialog>();
        var vm = (ReceiveBatchViewModel)window.DataContext;
        await vm.InitializeAsync(_itemId, branchId.Value);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadBatchesAsync();
    }

    private async Task AdjustStockAsync()
    {
        if (SelectedBatch is null) return;

        var window = _dialogService.CreateDialog<StockAdjustmentDialog>();
        var vm = (StockAdjustmentViewModel)window.DataContext;
        vm.Load(SelectedBatch.Id, $"{SelectedBatch.ItemName} — دفعة {SelectedBatch.BatchNumber}", SelectedBatch.QuantityOnHand);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadBatchesAsync();
    }
}
