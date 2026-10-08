using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Purchasing;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

public class SuppliersViewModel : ViewModelBase
{
    private readonly IPurchasingService _purchasingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private SupplierDto? _selectedSupplier;
    private bool _isBusy;

    public SuppliersViewModel(IPurchasingService purchasingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _purchasingService = purchasingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Suppliers = new ObservableCollection<SupplierDto>();

        RefreshCommand = new AsyncRelayCommand(LoadSuppliersAsync, () => !IsBusy);
        AddSupplierCommand = new AsyncRelayCommand(AddSupplierAsync, () => !IsBusy && CanManage);
        EditSupplierCommand = new AsyncRelayCommand(EditSupplierAsync, () => !IsBusy && CanManage && SelectedSupplier is not null);
        ToggleStatusCommand = new AsyncRelayCommand(ToggleStatusAsync, () => !IsBusy && CanManage && SelectedSupplier is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Purchasing.ManageSuppliers");

    public ObservableCollection<SupplierDto> Suppliers { get; }

    public SupplierDto? SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (SetProperty(ref _selectedSupplier, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetProperty(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddSupplierCommand { get; }
    public AsyncRelayCommand EditSupplierCommand { get; }
    public AsyncRelayCommand ToggleStatusCommand { get; }

    public async Task InitializeAsync() => await LoadSuppliersAsync();

    private async Task LoadSuppliersAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var suppliers = await _purchasingService.GetSuppliersAsync();
            Suppliers.Clear();
            foreach (var s in suppliers) Suppliers.Add(s);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddSupplierAsync()
    {
        var window = _dialogService.CreateDialog<SupplierEditDialog>();
        var vm = (SupplierEditViewModel)window.DataContext;
        vm.LoadForCreate();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadSuppliersAsync();
    }

    private async Task EditSupplierAsync()
    {
        if (SelectedSupplier is null) return;

        var window = _dialogService.CreateDialog<SupplierEditDialog>();
        var vm = (SupplierEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedSupplier.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadSuppliersAsync();
    }

    private async Task ToggleStatusAsync()
    {
        if (SelectedSupplier is null) return;

        var activate = !SelectedSupplier.IsActive;
        var message = activate
            ? $"هل تريد تفعيل المورد '{SelectedSupplier.Name}'؟"
            : $"هل تريد تعطيل المورد '{SelectedSupplier.Name}'؟";

        if (!_dialogService.Confirm(message)) return;

        var result = await _purchasingService.SetSupplierActiveStatusAsync(SelectedSupplier.Id, activate);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية.");
            return;
        }

        await LoadSuppliersAsync();
    }
}
