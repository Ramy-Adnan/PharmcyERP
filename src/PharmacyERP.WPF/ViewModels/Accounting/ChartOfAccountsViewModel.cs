using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Accounting;

namespace PharmacyERP.WPF.ViewModels.Accounting;

public class ChartOfAccountsViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private ChartOfAccountDto? _selectedAccount;
    private bool _isBusy;

    public ChartOfAccountsViewModel(IAccountingService accountingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _accountingService = accountingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Accounts = new ObservableCollection<ChartOfAccountDto>();

        RefreshCommand = new AsyncRelayCommand(LoadAccountsAsync);
        AddAccountCommand = new AsyncRelayCommand(AddAccountAsync, () => CanManage);
        EditAccountCommand = new AsyncRelayCommand(EditAccountAsync, () => CanManage && SelectedAccount is not null && !SelectedAccount.IsSystemAccount);
        ToggleStatusCommand = new AsyncRelayCommand(ToggleStatusAsync, () => CanManage && SelectedAccount is not null && !SelectedAccount.IsSystemAccount);
    }

    public bool CanManage => _currentUserService.HasPermission("Accounting.ManageChartOfAccounts");

    public ObservableCollection<ChartOfAccountDto> Accounts { get; }

    public ChartOfAccountDto? SelectedAccount
    {
        get => _selectedAccount;
        set { if (SetProperty(ref _selectedAccount, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddAccountCommand { get; }
    public AsyncRelayCommand EditAccountCommand { get; }
    public AsyncRelayCommand ToggleStatusCommand { get; }

    public async Task InitializeAsync() => await LoadAccountsAsync();

    private async Task LoadAccountsAsync()
    {
        IsBusy = true;
        try
        {
            var accounts = await _accountingService.GetAccountsAsync();
            Accounts.Clear();
            foreach (var a in accounts) Accounts.Add(a);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddAccountAsync()
    {
        var window = _dialogService.CreateDialog<ChartOfAccountEditDialog>();
        var vm = (ChartOfAccountEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAccountsAsync();
    }

    private async Task EditAccountAsync()
    {
        if (SelectedAccount is null) return;

        var window = _dialogService.CreateDialog<ChartOfAccountEditDialog>();
        var vm = (ChartOfAccountEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedAccount.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAccountsAsync();
    }

    private async Task ToggleStatusAsync()
    {
        if (SelectedAccount is null) return;

        var activate = !SelectedAccount.IsActive;
        var message = activate ? $"هل تريد تفعيل الحساب '{SelectedAccount.Name}'؟" : $"هل تريد تعطيل الحساب '{SelectedAccount.Name}'؟";
        if (!_dialogService.Confirm(message)) return;

        var result = await _accountingService.SetAccountActiveStatusAsync(SelectedAccount.Id, activate);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية.");
            return;
        }

        await LoadAccountsAsync();
    }
}
