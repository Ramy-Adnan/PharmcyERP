using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Insurance;
using PharmacyERP.Application.Features.Insurance.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Insurance;

namespace PharmacyERP.WPF.ViewModels.Insurance;

public class InsuranceCompaniesViewModel : ViewModelBase
{
    private readonly IInsuranceService _insuranceService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private InsuranceCompanyDto? _selectedCompany;
    private bool _isBusy;

    public InsuranceCompaniesViewModel(IInsuranceService insuranceService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _insuranceService = insuranceService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Companies = new ObservableCollection<InsuranceCompanyDto>();

        RefreshCommand = new AsyncRelayCommand(LoadCompaniesAsync);
        AddCompanyCommand = new AsyncRelayCommand(AddCompanyAsync, () => CanManage);
        EditCompanyCommand = new AsyncRelayCommand(EditCompanyAsync, () => CanManage && SelectedCompany is not null);
        ToggleStatusCommand = new AsyncRelayCommand(ToggleStatusAsync, () => CanManage && SelectedCompany is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Insurance.ManageCompanies");

    public ObservableCollection<InsuranceCompanyDto> Companies { get; }

    public InsuranceCompanyDto? SelectedCompany
    {
        get => _selectedCompany;
        set { if (SetProperty(ref _selectedCompany, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddCompanyCommand { get; }
    public AsyncRelayCommand EditCompanyCommand { get; }
    public AsyncRelayCommand ToggleStatusCommand { get; }

    public async Task InitializeAsync() => await LoadCompaniesAsync();

    private async Task LoadCompaniesAsync()
    {
        IsBusy = true;
        try
        {
            var companies = await _insuranceService.GetCompaniesAsync();
            Companies.Clear();
            foreach (var c in companies) Companies.Add(c);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddCompanyAsync()
    {
        var window = _dialogService.CreateDialog<InsuranceCompanyEditDialog>();
        var vm = (InsuranceCompanyEditViewModel)window.DataContext;
        vm.LoadForCreate();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadCompaniesAsync();
    }

    private async Task EditCompanyAsync()
    {
        if (SelectedCompany is null) return;

        var window = _dialogService.CreateDialog<InsuranceCompanyEditDialog>();
        var vm = (InsuranceCompanyEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedCompany.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadCompaniesAsync();
    }

    private async Task ToggleStatusAsync()
    {
        if (SelectedCompany is null) return;

        var activate = !SelectedCompany.IsActive;
        var message = activate ? $"هل تريد تفعيل شركة '{SelectedCompany.Name}'؟" : $"هل تريد تعطيل شركة '{SelectedCompany.Name}'؟";
        if (!_dialogService.Confirm(message)) return;

        var result = await _insuranceService.SetCompanyActiveStatusAsync(SelectedCompany.Id, activate);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية.");
            return;
        }

        await LoadCompaniesAsync();
    }
}
