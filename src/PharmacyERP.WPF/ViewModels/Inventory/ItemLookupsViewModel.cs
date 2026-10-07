using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Inventory;

/// <summary>
/// Hosts three inline-editable grids (Categories, Units of Measure,
/// Manufacturers) behind a tabbed UI. Each row is edited directly in the
/// DataGrid; "حفظ التغييرات" persists every dirty/new row in one pass, and
/// "حذف" removes the selected row immediately (the server rejects deletion
/// of lookups still referenced by an Item).
/// </summary>
public class ItemLookupsViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private EditableLookupRow? _selectedCategory;
    private EditableLookupRow? _selectedUnit;
    private EditableLookupRow? _selectedManufacturer;
    private bool _isBusy;

    public ItemLookupsViewModel(IInventoryService inventoryService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Categories = new ObservableCollection<EditableLookupRow>();
        Units = new ObservableCollection<EditableLookupRow>();
        Manufacturers = new ObservableCollection<EditableLookupRow>();

        RefreshCommand = new AsyncRelayCommand(LoadAllAsync);

        AddCategoryCommand = new RelayCommand(() => Categories.Add(new EditableLookupRow { IsActive = true, IsDirty = true }), () => CanManage);
        SaveCategoriesCommand = new AsyncRelayCommand(SaveCategoriesAsync, () => CanManage);
        DeleteCategoryCommand = new AsyncRelayCommand(DeleteCategoryAsync, () => CanManage && SelectedCategory is not null && !SelectedCategory.IsNew);

        AddUnitCommand = new RelayCommand(() => Units.Add(new EditableLookupRow { IsActive = true, IsDirty = true }), () => CanManage);
        SaveUnitsCommand = new AsyncRelayCommand(SaveUnitsAsync, () => CanManage);
        DeleteUnitCommand = new AsyncRelayCommand(DeleteUnitAsync, () => CanManage && SelectedUnit is not null && !SelectedUnit.IsNew);

        AddManufacturerCommand = new RelayCommand(() => Manufacturers.Add(new EditableLookupRow { IsActive = true, IsDirty = true }), () => CanManage);
        SaveManufacturersCommand = new AsyncRelayCommand(SaveManufacturersAsync, () => CanManage);
        DeleteManufacturerCommand = new AsyncRelayCommand(DeleteManufacturerAsync, () => CanManage && SelectedManufacturer is not null && !SelectedManufacturer.IsNew);
    }

    public bool CanManage => _currentUserService.HasPermission("Inventory.ManageLookups");

    public ObservableCollection<EditableLookupRow> Categories { get; }
    public ObservableCollection<EditableLookupRow> Units { get; }
    public ObservableCollection<EditableLookupRow> Manufacturers { get; }

    public EditableLookupRow? SelectedCategory { get => _selectedCategory; set { if (SetProperty(ref _selectedCategory, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public EditableLookupRow? SelectedUnit { get => _selectedUnit; set { if (SetProperty(ref _selectedUnit, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public EditableLookupRow? SelectedManufacturer { get => _selectedManufacturer; set { if (SetProperty(ref _selectedManufacturer, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }

    public RelayCommand AddCategoryCommand { get; }
    public AsyncRelayCommand SaveCategoriesCommand { get; }
    public AsyncRelayCommand DeleteCategoryCommand { get; }

    public RelayCommand AddUnitCommand { get; }
    public AsyncRelayCommand SaveUnitsCommand { get; }
    public AsyncRelayCommand DeleteUnitCommand { get; }

    public RelayCommand AddManufacturerCommand { get; }
    public AsyncRelayCommand SaveManufacturersCommand { get; }
    public AsyncRelayCommand DeleteManufacturerCommand { get; }

    public async Task InitializeAsync() => await LoadAllAsync();

    private async Task LoadAllAsync()
    {
        IsBusy = true;
        try
        {
            var categories = await _inventoryService.GetCategoriesAsync();
            Categories.Clear();
            foreach (var c in categories)
                Categories.Add(new EditableLookupRow { Id = c.Id, Code = c.Code, Name = c.Name, IsActive = c.IsActive, ItemCount = c.ItemCount, IsDirty = false });

            var units = await _inventoryService.GetUnitsAsync();
            Units.Clear();
            foreach (var u in units)
                Units.Add(new EditableLookupRow { Id = u.Id, Code = u.Code, Name = u.Name, IsActive = u.IsActive, ItemCount = u.ItemCount, IsDirty = false });

            var manufacturers = await _inventoryService.GetManufacturersAsync();
            Manufacturers.Clear();
            foreach (var m in manufacturers)
                Manufacturers.Add(new EditableLookupRow { Id = m.Id, Name = m.Name, Extra = m.Country, IsActive = m.IsActive, ItemCount = m.ItemCount, IsDirty = false });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveCategoriesAsync()
    {
        foreach (var row in Categories.Where(r => r.IsDirty).ToList())
        {
            var result = await _inventoryService.UpsertCategoryAsync(row.IsNew ? null : row.Id, row.Code, row.Name, row.IsActive);
            if (!result.Succeeded)
            {
                _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حفظ التصنيف.");
                return;
            }
        }
        await LoadAllAsync();
    }

    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory is null) return;
        if (!_dialogService.Confirm($"هل أنت متأكد من حذف التصنيف '{SelectedCategory.Name}'؟")) return;

        var result = await _inventoryService.DeleteCategoryAsync(SelectedCategory.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف التصنيف.");
            return;
        }
        await LoadAllAsync();
    }

    private async Task SaveUnitsAsync()
    {
        foreach (var row in Units.Where(r => r.IsDirty).ToList())
        {
            var result = await _inventoryService.UpsertUnitAsync(row.IsNew ? null : row.Id, row.Code, row.Name, row.IsActive);
            if (!result.Succeeded)
            {
                _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حفظ الوحدة.");
                return;
            }
        }
        await LoadAllAsync();
    }

    private async Task DeleteUnitAsync()
    {
        if (SelectedUnit is null) return;
        if (!_dialogService.Confirm($"هل أنت متأكد من حذف الوحدة '{SelectedUnit.Name}'؟")) return;

        var result = await _inventoryService.DeleteUnitAsync(SelectedUnit.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف الوحدة.");
            return;
        }
        await LoadAllAsync();
    }

    private async Task SaveManufacturersAsync()
    {
        foreach (var row in Manufacturers.Where(r => r.IsDirty).ToList())
        {
            var result = await _inventoryService.UpsertManufacturerAsync(row.IsNew ? null : row.Id, row.Name, row.Extra, row.IsActive);
            if (!result.Succeeded)
            {
                _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حفظ الشركة المصنّعة.");
                return;
            }
        }
        await LoadAllAsync();
    }

    private async Task DeleteManufacturerAsync()
    {
        if (SelectedManufacturer is null) return;
        if (!_dialogService.Confirm($"هل أنت متأكد من حذف الشركة '{SelectedManufacturer.Name}'؟")) return;

        var result = await _inventoryService.DeleteManufacturerAsync(SelectedManufacturer.Id);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر حذف الشركة.");
            return;
        }
        await LoadAllAsync();
    }
}
