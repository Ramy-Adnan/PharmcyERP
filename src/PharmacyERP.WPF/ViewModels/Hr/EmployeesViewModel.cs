using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Hr;

namespace PharmacyERP.WPF.ViewModels.Hr;

public class EmployeesViewModel : ViewModelBase
{
    private readonly IHrService _hrService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private EmployeeDto? _selectedEmployee;
    private bool _isBusy;

    public EmployeesViewModel(IHrService hrService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _hrService = hrService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Employees = new ObservableCollection<EmployeeDto>();

        RefreshCommand = new AsyncRelayCommand(LoadEmployeesAsync);
        AddEmployeeCommand = new AsyncRelayCommand(AddEmployeeAsync, () => CanManage);
        EditEmployeeCommand = new AsyncRelayCommand(EditEmployeeAsync, () => CanManage && SelectedEmployee is not null);
        ToggleStatusCommand = new AsyncRelayCommand(ToggleStatusAsync, () => CanManage && SelectedEmployee is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Hr.ManageEmployees");

    public ObservableCollection<EmployeeDto> Employees { get; }

    public EmployeeDto? SelectedEmployee
    {
        get => _selectedEmployee;
        set { if (SetProperty(ref _selectedEmployee, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddEmployeeCommand { get; }
    public AsyncRelayCommand EditEmployeeCommand { get; }
    public AsyncRelayCommand ToggleStatusCommand { get; }

    public async Task InitializeAsync() => await LoadEmployeesAsync();

    private async Task LoadEmployeesAsync()
    {
        IsBusy = true;
        try
        {
            var employees = await _hrService.GetEmployeesAsync();
            Employees.Clear();
            foreach (var e in employees) Employees.Add(e);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddEmployeeAsync()
    {
        var window = _dialogService.CreateDialog<EmployeeEditDialog>();
        var vm = (EmployeeEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadEmployeesAsync();
    }

    private async Task EditEmployeeAsync()
    {
        if (SelectedEmployee is null) return;

        var window = _dialogService.CreateDialog<EmployeeEditDialog>();
        var vm = (EmployeeEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedEmployee.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadEmployeesAsync();
    }

    private async Task ToggleStatusAsync()
    {
        if (SelectedEmployee is null) return;

        var activate = !SelectedEmployee.IsActive;
        var message = activate
            ? $"هل تريد إعادة تفعيل الموظف '{SelectedEmployee.FullName}'؟"
            : $"هل تريد إنهاء خدمة الموظف '{SelectedEmployee.FullName}'؟";
        if (!_dialogService.Confirm(message)) return;

        var result = await _hrService.SetEmployeeActiveStatusAsync(SelectedEmployee.Id, activate);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية.");
            return;
        }

        await LoadEmployeesAsync();
    }
}
