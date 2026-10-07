using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Hr;

namespace PharmacyERP.WPF.ViewModels.Hr;

public class PayrollRunsViewModel : ViewModelBase
{
    private readonly IHrService _hrService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private PayrollRunDto? _selectedRun;
    private bool _isBusy;

    public PayrollRunsViewModel(IHrService hrService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _hrService = hrService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        PayrollRuns = new ObservableCollection<PayrollRunDto>();

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        GenerateRunCommand = new AsyncRelayCommand(GenerateRunAsync, () => CanManage);
        OpenDetailCommand = new AsyncRelayCommand(OpenDetailAsync, () => SelectedRun is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Hr.ManagePayroll");

    public ObservableCollection<PayrollRunDto> PayrollRuns { get; }

    public PayrollRunDto? SelectedRun
    {
        get => _selectedRun;
        set { if (SetProperty(ref _selectedRun, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand GenerateRunCommand { get; }
    public AsyncRelayCommand OpenDetailCommand { get; }

    public async Task InitializeAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var runs = await _hrService.GetPayrollRunsAsync();
            PayrollRuns.Clear();
            foreach (var r in runs) PayrollRuns.Add(r);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GenerateRunAsync()
    {
        var window = _dialogService.CreateDialog<GeneratePayrollRunDialog>();
        var vm = (GeneratePayrollRunViewModel)window.DataContext;
        await vm.LoadAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAsync();
    }

    private async Task OpenDetailAsync()
    {
        if (SelectedRun is null) return;

        var window = _dialogService.CreateDialog<PayrollRunDetailDialog>();
        var vm = (PayrollRunDetailViewModel)window.DataContext;
        await vm.LoadAsync(SelectedRun.Id);

        _dialogService.ShowDialog(window);
        await LoadAsync();
    }
}
