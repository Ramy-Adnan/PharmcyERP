using System.Collections.ObjectModel;
using System.Globalization;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Inventory;

namespace PharmacyERP.WPF.ViewModels.Inventory;

/// <summary>Batch balances, price maintenance and transaction history for one item.</summary>
public class ItemBatchesViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private int _itemId, _historyVersion;
    private string _itemName = string.Empty, _priceReason = string.Empty, _status = string.Empty;
    private BatchDto? _selectedBatch;
    private bool _isBusy, _closing;
    private string _saleUnitPriceText = "0";

    public ItemBatchesViewModel(IInventoryService inventoryService, IBranchService branchService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;
        RefreshCommand = new AsyncRelayCommand(LoadBatchesAsync, () => !IsBusy);
        ReceiveStockCommand = new AsyncRelayCommand(ReceiveStockAsync, () => !IsBusy && CanReceiveStock);
        AdjustStockCommand = new AsyncRelayCommand(AdjustStockAsync, () => !IsBusy && CanAdjustStock && SelectedBatch is not null);
        SavePriceCommand = new AsyncRelayCommand(SavePriceAsync, () => !IsBusy && CanEditPrice && SelectedBatch is { QuantityOnHand: > 0, IsExpired: false }
            && SaleUnitPrice >= 0.005m && SaleUnitPrice <= 9999999999999999.99m && !string.IsNullOrWhiteSpace(PriceReason));
        SuggestPriceCommand = new RelayCommand(() => SaleUnitPrice = PharmacyERP.Domain.Common.SalePricePolicy.FromPurchasePrice(
            SelectedBatch!.PurchasePrice * SelectedBatch.SaleUnitFactor, SelectedBatch.PurchaseType), () => !IsBusy && SelectedBatch is not null && CanEditPrice);
    }

    public bool CanReceiveStock => _currentUserService.HasPermission("Inventory.ReceiveStock");
    public bool CanAdjustStock => _currentUserService.HasPermission("Inventory.AdjustStock");
    public bool CanEditPrice => _currentUserService.HasPermission("Inventory.ManageItems");
    public string ItemName { get => _itemName; private set => SetProperty(ref _itemName, value); }
    public ObservableCollection<BatchDto> Batches { get; } = new();
    public ObservableCollection<StockTransactionDto> TransactionHistory { get; } = new();
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PriceReason { get => _priceReason; set { if (SetProperty(ref _priceReason, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public IReadOnlyList<string> PriceReasons { get; } = new[] { "تحديث سعر المورد", "تصحيح سعر البيع", "عرض أو تسعير جديد" };
    public string SaleUnitPriceText
    {
        get => _saleUnitPriceText;
        set
        {
            if (!SetProperty(ref _saleUnitPriceText, value)) return;
            OnPropertyChanged(nameof(BaseUnitPrice));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public decimal SaleUnitPrice
    {
        get => decimal.TryParse(SaleUnitPriceText, NumberStyles.Number, CultureInfo.CurrentCulture, out var price) ? price : 0;
        set => SaleUnitPriceText = value.ToString("0.##", CultureInfo.CurrentCulture);
    }
    public decimal BaseUnitPrice => SelectedBatch is { SaleUnitFactor: > 0 } batch ? Math.Round(SaleUnitPrice / batch.SaleUnitFactor, 2, MidpointRounding.AwayFromZero) : 0;
    public BatchDto? SelectedBatch
    {
        get => _selectedBatch;
        set
        {
            if (!SetProperty(ref _selectedBatch, value)) return;
            SaleUnitPrice = value?.SaleUnitPrice ?? 0; PriceReason = string.Empty;
            OnPropertyChanged(nameof(BaseUnitPrice));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            _ = LoadHistoryAsync(value, ++_historyVersion);
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set { SetProperty(ref _isBusy, value); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ReceiveStockCommand { get; }
    public AsyncRelayCommand AdjustStockCommand { get; }
    public AsyncRelayCommand SavePriceCommand { get; }
    public RelayCommand SuggestPriceCommand { get; }

    public async Task InitializeAsync(int itemId, string itemName)
    {
        _itemId = itemId; ItemName = itemName;
        await LoadBatchesAsync();
    }

    private async Task LoadBatchesCoreAsync(int? selectedId)
    {
        var batches = await _inventoryService.GetBatchesForItemAsync(_itemId);
        Batches.Clear();
        foreach (var batch in batches) Batches.Add(batch);
        SelectedBatch = Batches.FirstOrDefault(b => b.Id == selectedId) ?? Batches.FirstOrDefault(b => !b.IsExpired && b.QuantityOnHand > 0);
        if (SelectedBatch is null) { TransactionHistory.Clear(); _historyVersion++; }
    }

    private async Task LoadBatchesAsync()
    {
        if (_closing) return;
        IsBusy = true;
        await _operations.WaitAsync();
        try { await LoadBatchesCoreAsync(SelectedBatch?.Id); }
        catch (Exception ex) { Status = $"تعذر تحميل الدفعات: {ex.Message}"; }
        finally { _operations.Release(); IsBusy = false; }
    }

    private async Task LoadHistoryAsync(BatchDto? batch, int version)
    {
        TransactionHistory.Clear();
        if (batch is null || _closing) return;
        await _operations.WaitAsync();
        try
        {
            if (version != _historyVersion) return;
            var history = await _inventoryService.GetTransactionHistoryAsync(batch.Id);
            if (version != _historyVersion) return;
            foreach (var row in history) TransactionHistory.Add(row);
        }
        catch (Exception ex) { if (version == _historyVersion) Status = $"تعذر تحميل الحركات: {ex.Message}"; }
        finally { _operations.Release(); }
    }

    public async Task SavePriceAsync()
    {
        var batch = SelectedBatch;
        if (_closing || IsBusy || !CanEditPrice || batch is null) return;
        if (SaleUnitPrice <= 0 || string.IsNullOrWhiteSpace(PriceReason)) { Status = "أدخل سعر بيع أكبر من صفر وسبب التعديل."; return; }
        var request = new UpdateBatchSalePriceDto { BatchId = batch.Id, SaleUnitPrice = SaleUnitPrice,
            ExpectedSaleUnitPrice = batch.SaleUnitPrice, ExpectedRowVersion = batch.RowVersion, Reason = PriceReason };
        IsBusy = true;
        await _operations.WaitAsync();
        try
        {
            var result = await _inventoryService.UpdateBatchSalePriceAsync(request);
            if (!result.Succeeded) { Status = string.Join("\n", result.Errors); return; }
            var index = Batches.IndexOf(batch);
            if (index >= 0) Batches[index] = result.Value!;
            SelectedBatch = result.Value;
            Status = $"تم حفظ سعر {result.Value!.SaleUnitName}: {result.Value.SaleUnitPrice:N2} د.ع. الفواتير السابقة لم تتغير.";
        }
        catch (Exception ex) { Status = $"تعذر حفظ السعر: {ex.Message}"; }
        finally { _operations.Release(); IsBusy = false; }
    }

    private async Task ReceiveStockAsync()
    {
        if (_closing || IsBusy || !CanReceiveStock) return;
        if (_currentUserService.CurrentBranchId is not int branchId) { _dialogService.ShowError("تعذر تحديد الفرع الحالي."); return; }
        IsBusy = true;
        await _operations.WaitAsync();
        try
        {
            var window = _dialogService.CreateDialog<ReceiveBatchDialog>();
            var vm = (ReceiveBatchViewModel)window.DataContext;
            await vm.InitializeAsync(_itemId, branchId);
            _dialogService.ShowDialog(window);
            if (vm.SavedSuccessfully) await LoadBatchesCoreAsync(SelectedBatch?.Id);
        }
        catch (Exception ex) { _dialogService.ShowError($"تعذر استلام البضاعة: {ex.Message}"); }
        finally { _operations.Release(); IsBusy = false; }
    }

    private async Task AdjustStockAsync()
    {
        if (_closing || IsBusy || !CanAdjustStock || SelectedBatch is not { } batch) return;
        IsBusy = true;
        await _operations.WaitAsync();
        try
        {
            var window = _dialogService.CreateDialog<StockAdjustmentDialog>();
            var vm = (StockAdjustmentViewModel)window.DataContext;
            vm.Load(batch.Id, $"{batch.ItemName} — دفعة {batch.BatchNumber}", batch.QuantityOnHand);
            if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully) await LoadBatchesCoreAsync(batch.Id);
        }
        catch (Exception ex) { _dialogService.ShowError($"تعذر تعديل الرصيد: {ex.Message}"); }
        finally { _operations.Release(); IsBusy = false; }
    }

    public async Task PrepareToCloseAsync()
    {
        _closing = true;
        _historyVersion++;
        await _operations.WaitAsync();
        _operations.Release();
    }
}
