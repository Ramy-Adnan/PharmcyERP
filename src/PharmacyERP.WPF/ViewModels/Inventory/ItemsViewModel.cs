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
    private string _searchText = string.Empty;
    private bool _isBusy;

    public ItemsViewModel(IInventoryService inventoryService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Items = new ObservableCollection<ItemDto>();

        SearchCommand = new AsyncRelayCommand(LoadItemsAsync);
        AddItemCommand = new AsyncRelayCommand(AddItemAsync, () => CanManageItems);
        EditItemCommand = new AsyncRelayCommand(EditItemAsync, () => CanManageItems && SelectedItem is not null);
        ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => CanManageItems && SelectedItem is not null);
        ManageBatchesCommand = new AsyncRelayCommand(ManageBatchesAsync, () => SelectedItem is not null);
    }

    public bool CanManageItems => _currentUserService.HasPermission("Inventory.ManageItems");
    public bool CanReceiveStock => _currentUserService.HasPermission("Inventory.ReceiveStock");
    public bool CanAdjustStock => _currentUserService.HasPermission("Inventory.AdjustStock");

    public ObservableCollection<ItemDto> Items { get; }

    public ItemDto? SelectedItem
    {
        get => _selectedItem;
        set { if (SetProperty(ref _selectedItem, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand AddItemCommand { get; }
    public AsyncRelayCommand EditItemCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand ManageBatchesCommand { get; }

    public async Task InitializeAsync() => await LoadItemsAsync();

    private async Task LoadItemsAsync()
    {
        IsBusy = true;
        try
        {
            var items = await _inventoryService.GetItemsAsync(SearchText);
            Items.Clear();
            foreach (var i in items) Items.Add(i);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddItemAsync()
    {
        var window = _dialogService.CreateDialog<ItemEditDialog>();
        var vm = (ItemEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadItemsAsync();
    }

    private async Task EditItemAsync()
    {
        if (SelectedItem is null) return;

        var window = _dialogService.CreateDialog<ItemEditDialog>();
        var vm = (ItemEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedItem.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadItemsAsync();
    }

    private async Task ToggleActiveAsync()
    {
        if (SelectedItem is null) return;

        var activate = !SelectedItem.IsActive;
        var message = activate ? $"هل تريد تفعيل الصنف '{SelectedItem.Name}'؟" : $"هل تريد تعطيل الصنف '{SelectedItem.Name}'؟";
        if (!_dialogService.Confirm(message)) return;

        var result = await _inventoryService.SetItemActiveStatusAsync(SelectedItem.Id, activate);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية.");
            return;
        }

        await LoadItemsAsync();
    }

    private async Task ManageBatchesAsync()
    {
        if (SelectedItem is null) return;

        var window = _dialogService.CreateDialog<ItemBatchesDialog>();
        var vm = (ItemBatchesViewModel)window.DataContext;
        await vm.InitializeAsync(SelectedItem.Id, SelectedItem.Name);

        _dialogService.ShowDialog(window);
        await LoadItemsAsync(); // totals (TotalQuantityOnHand) may have changed
    }
}
