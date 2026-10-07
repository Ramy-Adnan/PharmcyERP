using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Sales;

namespace PharmacyERP.WPF.ViewModels.Sales;

public class CustomersViewModel : ViewModelBase
{
    private readonly ISalesService _salesService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private CustomerDto? _selectedCustomer;
    private bool _isBusy;

    public CustomersViewModel(ISalesService salesService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _salesService = salesService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Customers = new ObservableCollection<CustomerDto>();

        AccountCommand = new AsyncRelayCommand(OpenAccountAsync, () => SelectedCustomer is not null);
        RefreshCommand = new AsyncRelayCommand(LoadCustomersAsync);
        AddCustomerCommand = new AsyncRelayCommand(AddCustomerAsync, () => CanManage);
        EditCustomerCommand = new AsyncRelayCommand(EditCustomerAsync, () => CanManage && SelectedCustomer is not null);
        ToggleStatusCommand = new AsyncRelayCommand(ToggleStatusAsync, () => CanManage && SelectedCustomer is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Sales.ManageCustomers");

    public ObservableCollection<CustomerDto> Customers { get; }

    public CustomerDto? SelectedCustomer
    {
        get => _selectedCustomer;
        set { if (SetProperty(ref _selectedCustomer, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand AccountCommand { get; }

    private async Task OpenAccountAsync()
    {
        if (SelectedCustomer is null) return;
        var window = _dialogService.CreateDialog<CustomerAccountDialog>();
        await ((CustomerAccountViewModel)window.DataContext).LoadAsync(SelectedCustomer.Id);
        _dialogService.ShowDialog(window);
        await LoadCustomersAsync();
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddCustomerCommand { get; }
    public AsyncRelayCommand EditCustomerCommand { get; }
    public AsyncRelayCommand ToggleStatusCommand { get; }

    public async Task InitializeAsync() => await LoadCustomersAsync();

    private async Task LoadCustomersAsync()
    {
        IsBusy = true;
        try
        {
            var customers = await _salesService.GetCustomersAsync();
            Customers.Clear();
            foreach (var c in customers) Customers.Add(c);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddCustomerAsync()
    {
        var window = _dialogService.CreateDialog<CustomerEditDialog>();
        var vm = (CustomerEditViewModel)window.DataContext;
        vm.LoadForCreate();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadCustomersAsync();
    }

    private async Task EditCustomerAsync()
    {
        if (SelectedCustomer is null) return;

        var window = _dialogService.CreateDialog<CustomerEditDialog>();
        var vm = (CustomerEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedCustomer.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadCustomersAsync();
    }

    private async Task ToggleStatusAsync()
    {
        if (SelectedCustomer is null) return;

        var activate = !SelectedCustomer.IsActive;
        var message = activate ? $"هل تريد تفعيل العميل '{SelectedCustomer.Name}'؟" : $"هل تريد تعطيل العميل '{SelectedCustomer.Name}'؟";
        if (!_dialogService.Confirm(message)) return;

        var result = await _salesService.SetCustomerActiveStatusAsync(SelectedCustomer.Id, activate);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية.");
            return;
        }

        await LoadCustomersAsync();
    }
}
