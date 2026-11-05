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
    private bool _isBusy, _initialized, _active, _editorOpen, _hasMore;
    private int _nextSkip, _searchVersion;
    private string _loadedSearch = string.Empty;
    private CancellationTokenSource? _searchDelay;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private const int PageSize = 60;

    public ItemsViewModel(IInventoryService inventoryService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService; _dialogService = dialogService; _currentUserService = currentUserService;
        SearchCommand = new AsyncRelayCommand(LoadItemsAsync, () => !IsBusy);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => !IsBusy && HasMore);
        AddItemCommand = new AsyncRelayCommand(() => RunAsync(AddItemCoreAsync), () => !IsBusy && CanManageItems);
        EditItemCommand = new AsyncRelayCommand(() => RunAsync(EditItemCoreAsync), () => !IsBusy && CanManageItems && SelectedItem is not null);
        ToggleActiveCommand = new AsyncRelayCommand(() => RunAsync(ToggleActiveCoreAsync), () => !IsBusy && CanManageItems && SelectedItem is not null);
        ManageBatchesCommand = new AsyncRelayCommand(() => RunAsync(ManageBatchesCoreAsync), () => !IsBusy && SelectedItem is not null);
        PrintLabelsCommand = new AsyncRelayCommand(() => RunAsync(PrintLabelsCoreAsync), () => !IsBusy && CanPrintLabels && SelectedItem is { IsActive: true });
        ReceiveStockCommand = new AsyncRelayCommand(() => RunAsync(() => ReceiveStockCoreAsync(SelectedItem)), () => !IsBusy && CanReceiveStock && SelectedItem is { IsActive: true });
    }

    public bool CanManageItems => _currentUserService.HasPermission("Inventory.ManageItems");
    public bool CanReceiveStock => _currentUserService.HasPermission("Inventory.ReceiveStock");
    public bool CanAdjustStock => _currentUserService.HasPermission("Inventory.AdjustStock");
    public bool CanPrintLabels => CanManageItems || _currentUserService.HasPermission("Inventory.ViewStock");
    public ObservableCollection<ItemDto> Items { get; } = new();
    public ItemDto? SelectedItem
    {
        get => _selectedItem;
        set { if (SetProperty(ref _selectedItem, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            CancelPendingSearch(); HasMore = false; SelectedItem = null;
            if (_initialized && _active && !_editorOpen) QueueSearch();
        }
    }
    public bool HasMore { get => _hasMore; private set { if (SetProperty(ref _hasMore, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public string ResultsSummary => Items.Count == 0 ? "لا توجد أصناف مطابقة؛ غيّر البحث أو أضف صنفاً جديداً."
        : $"المعروض: {Items.Count:N0} صنف" + (HasMore ? " — تتوفر نتائج أخرى" : string.Empty);
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand LoadMoreCommand { get; }
    public AsyncRelayCommand AddItemCommand { get; }
    public AsyncRelayCommand EditItemCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand ManageBatchesCommand { get; }
    public AsyncRelayCommand PrintLabelsCommand { get; }
    public AsyncRelayCommand ReceiveStockCommand { get; }

    public Task InitializeAsync()
    {
        if (_active && _initialized) return Task.CompletedTask;
        _active = _initialized = true;
        return LoadItemsAsync();
    }
    public void Suspend()
    { _active = false; CancelPendingSearch(); }

    public async Task WaitForIdleAsync()
    { await _operations.WaitAsync(); _operations.Release(); }

    private void CancelPendingSearch()
    { _searchVersion++; _searchDelay?.Cancel(); _searchDelay?.Dispose(); _searchDelay = null; }

    private void QueueSearch()
    {
        var delay = new CancellationTokenSource(); _searchDelay = delay;
        _ = DebouncedSearchAsync(_searchVersion, delay.Token);
    }
    private async Task DebouncedSearchAsync(int version, CancellationToken cancellationToken)
    {
        try { await Task.Delay(300, cancellationToken); await LoadPageAsync(false, version); }
        catch (OperationCanceledException) { }
    }
    private Task LoadItemsAsync()
    { CancelPendingSearch(); return LoadPageAsync(false, _searchVersion); }
    private Task LoadMoreAsync() => LoadPageAsync(true, _searchVersion);

    private async Task LoadPageAsync(bool append, int version)
    {
        await _operations.WaitAsync();
        try
        {
            if (!_active || _editorOpen || version != _searchVersion) return;
            var text = SearchText.Trim();
            if (append && (!HasMore || text != _loadedSearch)) return;
            IsBusy = true; ErrorMessage = string.Empty;
            var selectedId = SelectedItem?.Id;
            var page = await _inventoryService.GetItemsPageAsync(text, append ? _nextSkip : 0, PageSize);
            if (!_active || version != _searchVersion) return;
            if (!append) Items.Clear();
            foreach (var item in page.Items) if (!Items.Any(i => i.Id == item.Id)) Items.Add(item);
            _loadedSearch = text; _nextSkip = page.NextSkip; HasMore = page.HasMore;
            SelectedItem = Items.FirstOrDefault(i => i.Id == selectedId) ?? (Items.Count == 1 ? Items[0] : null);
            OnPropertyChanged(nameof(ResultsSummary));
        }
        catch (Exception ex) { if (_active && version == _searchVersion) ErrorMessage = $"تعذر تحميل الأصناف: {ex.Message}"; }
        finally { IsBusy = false; _operations.Release(); }
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (!_active || IsBusy) return;
        CancelPendingSearch(); _editorOpen = true; IsBusy = true; ErrorMessage = string.Empty;
        await _operations.WaitAsync();
        try { await operation(); }
        catch (Exception ex) { ErrorMessage = $"تعذر تنفيذ العملية: {ex.Message}"; }
        finally
        {
            _operations.Release(); _editorOpen = false; IsBusy = false;
            if (_active && SearchText.Trim() != _loadedSearch) QueueSearch();
        }
    }

    private async Task ReloadItemsCoreAsync()
    {
        var selectedId = SelectedItem?.Id;
        var text = SearchText.Trim();
        var page = await _inventoryService.GetItemsPageAsync(text, 0, PageSize);
        Items.Clear(); foreach (var item in page.Items) Items.Add(item);
        _loadedSearch = text; _nextSkip = page.NextSkip; HasMore = page.HasMore;
        SelectedItem = Items.FirstOrDefault(i => i.Id == selectedId);
        OnPropertyChanged(nameof(ResultsSummary));
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
        SearchText = vm.SavedItemCode;
        await ReloadItemsCoreAsync(); SelectedItem = Items.FirstOrDefault(i => i.Id == vm.SavedItemId);
        StatusMessage = $"تم تحديث بطاقة «{vm.SavedItemName}».";
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

    private async Task PrintLabelsCoreAsync()
    {
        if (SelectedItem is not { } item || !CanPrintLabels) return;
        var window = _dialogService.CreateDialog<PrintProductLabelsDialog>();
        await ((PrintProductLabelsViewModel)window.DataContext).InitializeAsync(item.Id, item.Code, item.DisplayName);
        _dialogService.ShowDialog(window);
    }
}
