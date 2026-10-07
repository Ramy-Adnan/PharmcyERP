using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Hr;

namespace PharmacyERP.WPF.ViewModels.Hr;

public class CommissionsViewModel : ViewModelBase
{
    private readonly IHrService _hrService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private bool _isBusy;

    public CommissionsViewModel(IHrService hrService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _hrService = hrService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Commissions = new ObservableCollection<CommissionDto>();

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AddCommissionCommand = new AsyncRelayCommand(AddCommissionAsync, () => CanManage);
    }

    public bool CanManage => _currentUserService.HasPermission("Hr.ManageCommissions");

    public ObservableCollection<CommissionDto> Commissions { get; }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddCommissionCommand { get; }

    public async Task InitializeAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var commissions = await _hrService.GetCommissionsAsync();
            Commissions.Clear();
            foreach (var c in commissions) Commissions.Add(c);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddCommissionAsync()
    {
        var window = _dialogService.CreateDialog<CommissionCreateDialog>();
        var vm = (CommissionCreateViewModel)window.DataContext;
        await vm.LoadAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAsync();
    }
}
