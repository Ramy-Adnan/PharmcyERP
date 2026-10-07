using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Insurance;
using PharmacyERP.Application.Features.Insurance.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Insurance;

namespace PharmacyERP.WPF.ViewModels.Insurance;

public class InsurancePoliciesViewModel : ViewModelBase
{
    private readonly IInsuranceService _insuranceService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private InsurancePolicyDto? _selectedPolicy;
    private bool _isBusy;

    public InsurancePoliciesViewModel(IInsuranceService insuranceService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _insuranceService = insuranceService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Policies = new ObservableCollection<InsurancePolicyDto>();

        RefreshCommand = new AsyncRelayCommand(LoadPoliciesAsync);
        AddPolicyCommand = new AsyncRelayCommand(AddPolicyAsync, () => CanManage);
        EditPolicyCommand = new AsyncRelayCommand(EditPolicyAsync, () => CanManage && SelectedPolicy is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Insurance.ManagePolicies");

    public ObservableCollection<InsurancePolicyDto> Policies { get; }

    public InsurancePolicyDto? SelectedPolicy
    {
        get => _selectedPolicy;
        set { if (SetProperty(ref _selectedPolicy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddPolicyCommand { get; }
    public AsyncRelayCommand EditPolicyCommand { get; }

    public async Task InitializeAsync() => await LoadPoliciesAsync();

    private async Task LoadPoliciesAsync()
    {
        IsBusy = true;
        try
        {
            var policies = await _insuranceService.GetPoliciesAsync();
            Policies.Clear();
            foreach (var p in policies) Policies.Add(p);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddPolicyAsync()
    {
        var window = _dialogService.CreateDialog<InsurancePolicyEditDialog>();
        var vm = (InsurancePolicyEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadPoliciesAsync();
    }

    private async Task EditPolicyAsync()
    {
        if (SelectedPolicy is null) return;

        var window = _dialogService.CreateDialog<InsurancePolicyEditDialog>();
        var vm = (InsurancePolicyEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedPolicy.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadPoliciesAsync();
    }
}
