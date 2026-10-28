using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Inventory;

namespace PharmacyERP.WPF.ViewModels.Inventory;

public class ItemsViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;
    private ItemDto? _selectedItem;
    private string _searchText = string.Empty, _statusMessage = string.Empty, _errorMessage = string.Empty;
    private bool _isBusy;

    public ItemsViewModel(IInventoryService inventoryService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService; _dialogService = dialogService; _currentUserService = currentUserService;
        SearchCommand = new AsyncRelayCommand(LoadItemsAsync, () => !IsBusy);
        AddItemCommand = new AsyncRelayCommand(() => RunAsync(AddItemCoreAsync), () => !IsBusy && CanManageItems);
        EditItemCommand = new AsyncRelayCommand(() => RunAsync(EditItemCoreAsync), () => !IsBusy && CanManageItems && SelectedItem is not null);
        ToggleActiveCommand = new AsyncRelayCommand(() => RunAsync(ToggleActiveCoreAsync), () => !IsBusy && CanManageItems && SelectedItem is not null);
        ManageBatchesCommand = new AsyncRelayCommand(() => RunAsync(ManageBatchesCoreAsync), () => !IsBusy && SelectedItem is not null);
        ReceiveStockCommand = new AsyncRelayCommand(() => RunAsync(() => ReceiveStockCoreAsync(SelectedItem)), () => !IsBusy && CanReceiveStock && SelectedItem is { IsActive: true });
    }

    public bool CanManageItems => _currentUserService.HasPermission("Inventory.ManageItems");
    public bool CanReceiveStock => _currentUserService.HasPermission("Inventory.ReceiveStock");
    public bool CanAdjustStock => _currentUserService.HasPermission("Inventory.AdjustStock");
    public ObservableCollection<ItemDto> Items { get; } = new();
    public ItemDto? SelectedItem
    {
        get => _selectedItem;
        set { if (SetProperty(ref _selectedItem, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand AddItemCommand { get; }
    public AsyncRelayCommand EditItemCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand ManageBatchesCommand { get; }
    public AsyncRelayCommand ReceiveStockCommand { get; }

    public Task InitializeAsync() => LoadItemsAsync();
    private Task LoadItemsAsync() => RunAsync(ReloadItemsCoreAsync);

    private async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true; ErrorMessage = string.Empty;
        try { await operation(); }
        catch (Exception ex) { ErrorMessage = $"تعذر تنفيذ العملية: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task ReloadItemsCoreAsync()
    {
        var selectedId = SelectedItem?.Id;
        var items = await _inventoryService.GetItemsAsync(SearchText);
        Items.Clear(); foreach (var item in items) Items.Add(item);
        SelectedItem = Items.FirstOrDefault(i => i.Id == selectedId);
    }

    private async Task AddItemCoreAsync()
    {
        var window = _dialogService.CreateDialog<ItemEditDialog>();
        var vm = (ItemEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();
        vm.CanReceiveStock = CanReceiveStock;
        var confirmed = _dialogService.ShowDialog(window) == true;
        if (!vm.SavedSuccessfully) return;
        SearchText = vm.SavedItemCode;
        await ReloadItemsCoreAsync();
        SelectedItem = Items.FirstOrDefault(i => i.Id == vm.SavedItemId);
        StatusMessage = $"تم حفظ الصنف «{vm.SavedItemName}». يمكنك استلام البضاعة مباشرة أو متابعة إدخال الأصناف.";
        if (confirmed && vm.ReceiveStockAfterSave && CanReceiveStock)
            await ReceiveStockCoreAsync(SelectedItem);
    }

    private async Task EditItemCoreAsync()
    {
        if (SelectedItem is null) return;
        var window = _dialogService.CreateDialog<ItemEditDialog>();
        var vm = (ItemEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedItem.Id);
        _dialogService.ShowDialog(window);
        if (!vm.SavedSuccessfully) return;
        await ReloadItemsCoreAsync(); StatusMessage = $"تم تحديث بطاقة «{vm.SavedItemName}».";
    }

    private async Task ToggleActiveCoreAsync()
    {
        if (SelectedItem is null) return;
        var activate = !SelectedItem.IsActive;
        if (!_dialogService.Confirm(activate ? $"هل تريد تفعيل الصنف '{SelectedItem.Name}'؟" : $"هل تريد تعطيل الصنف '{SelectedItem.Name}'؟")) return;
        var result = await _inventoryService.SetItemActiveStatusAsync(SelectedItem.Id, activate);
        if (!result.Succeeded) { ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية."; return; }
        await ReloadItemsCoreAsync(); StatusMessage = activate ? "تم تفعيل الصنف." : "تم تعطيل الصنف.";
    }

    private async Task ReceiveStockCoreAsync(ItemDto? item)
    {
        if (item is null || !CanReceiveStock) return;
        if (_currentUserService.CurrentBranchId is not { } branchId)
        { ErrorMessage = "اختر الفرع الحالي قبل استلام البضاعة."; return; }
        var window = _dialogService.CreateDialog<ReceiveBatchDialog>();
        var vm = (ReceiveBatchViewModel)window.DataContext;
        await vm.InitializeAsync(item.Id, branchId);
        _dialogService.ShowDialog(window);
        if (!vm.SavedSuccessfully) return;
        await ReloadItemsCoreAsync(); StatusMessage = $"تم استلام البضاعة للصنف «{item.Name}» وتحديث رصيد المخزن.";
    }

    private async Task ManageBatchesCoreAsync()
    {
        if (SelectedItem is null) return;
        var window = _dialogService.CreateDialog<ItemBatchesDialog>();
        var vm = (ItemBatchesViewModel)window.DataContext;
        await vm.InitializeAsync(SelectedItem.Id, SelectedItem.Name);
        _dialogService.ShowDialog(window); await ReloadItemsCoreAsync();
    }
}
