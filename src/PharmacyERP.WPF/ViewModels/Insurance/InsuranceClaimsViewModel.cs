using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Insurance;
using PharmacyERP.Application.Features.Insurance.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Insurance;

namespace PharmacyERP.WPF.ViewModels.Insurance;

public class InsuranceClaimsViewModel : ViewModelBase
{
    private readonly IInsuranceService _insuranceService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private InsuranceClaimDto? _selectedClaim;
    private bool _isBusy;

    public InsuranceClaimsViewModel(IInsuranceService insuranceService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _insuranceService = insuranceService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Claims = new ObservableCollection<InsuranceClaimDto>();

        RefreshCommand = new AsyncRelayCommand(LoadClaimsAsync);
        SubmitClaimCommand = new AsyncRelayCommand(SubmitClaimAsync, () => CanManage);
        ProcessClaimCommand = new AsyncRelayCommand(ProcessClaimAsync, () => CanManage && SelectedClaim is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Insurance.ManageClaims");

    public ObservableCollection<InsuranceClaimDto> Claims { get; }

    public InsuranceClaimDto? SelectedClaim
    {
        get => _selectedClaim;
        set { if (SetProperty(ref _selectedClaim, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SubmitClaimCommand { get; }
    public AsyncRelayCommand ProcessClaimCommand { get; }

    public async Task InitializeAsync() => await LoadClaimsAsync();

    private async Task LoadClaimsAsync()
    {
        IsBusy = true;
        try
        {
            var claims = await _insuranceService.GetClaimsAsync();
            Claims.Clear();
            foreach (var c in claims) Claims.Add(c);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SubmitClaimAsync()
    {
        var window = _dialogService.CreateDialog<SubmitClaimDialog>();
        var vm = (SubmitClaimViewModel)window.DataContext;
        await vm.LoadAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadClaimsAsync();
    }

    private async Task ProcessClaimAsync()
    {
        if (SelectedClaim is null) return;

        var window = _dialogService.CreateDialog<ProcessClaimDialog>();
        var vm = (ProcessClaimViewModel)window.DataContext;
        vm.Load(SelectedClaim);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadClaimsAsync();
    }
}
