using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Accounting;

namespace PharmacyERP.WPF.ViewModels.Accounting;

/// <summary>Combined screen for managing Cash Boxes and Bank Accounts side by side, since both are simple lookup-style registries used elsewhere (Expenses, Receipts, Payments) as a payment source.</summary>
public class CashAndBankViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private CashBoxDto? _selectedCashBox;
    private BankAccountDto? _selectedBankAccount;
    private bool _isBusy;

    public CashAndBankViewModel(IAccountingService accountingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _accountingService = accountingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        CashBoxes = new ObservableCollection<CashBoxDto>();
        BankAccounts = new ObservableCollection<BankAccountDto>();

        RefreshCommand = new AsyncRelayCommand(LoadAllAsync);
        AddCashBoxCommand = new AsyncRelayCommand(AddCashBoxAsync, () => CanManage);
        EditCashBoxCommand = new AsyncRelayCommand(EditCashBoxAsync, () => CanManage && SelectedCashBox is not null);
        AddBankAccountCommand = new AsyncRelayCommand(AddBankAccountAsync, () => CanManage);
        EditBankAccountCommand = new AsyncRelayCommand(EditBankAccountAsync, () => CanManage && SelectedBankAccount is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Accounting.ManageCashAndBank");

    public ObservableCollection<CashBoxDto> CashBoxes { get; }
    public ObservableCollection<BankAccountDto> BankAccounts { get; }

    public CashBoxDto? SelectedCashBox
    {
        get => _selectedCashBox;
        set { if (SetProperty(ref _selectedCashBox, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public BankAccountDto? SelectedBankAccount
    {
        get => _selectedBankAccount;
        set { if (SetProperty(ref _selectedBankAccount, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddCashBoxCommand { get; }
    public AsyncRelayCommand EditCashBoxCommand { get; }
    public AsyncRelayCommand AddBankAccountCommand { get; }
    public AsyncRelayCommand EditBankAccountCommand { get; }

    public async Task InitializeAsync() => await LoadAllAsync();

    private async Task LoadAllAsync()
    {
        IsBusy = true;
        try
        {
            var cashBoxes = await _accountingService.GetCashBoxesAsync();
            CashBoxes.Clear();
            foreach (var c in cashBoxes) CashBoxes.Add(c);

            var bankAccounts = await _accountingService.GetBankAccountsAsync();
            BankAccounts.Clear();
            foreach (var b in bankAccounts) BankAccounts.Add(b);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddCashBoxAsync()
    {
        var window = _dialogService.CreateDialog<CashBoxEditDialog>();
        var vm = (CashBoxEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAllAsync();
    }

    private async Task EditCashBoxAsync()
    {
        if (SelectedCashBox is null) return;

        var window = _dialogService.CreateDialog<CashBoxEditDialog>();
        var vm = (CashBoxEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedCashBox.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAllAsync();
    }

    private async Task AddBankAccountAsync()
    {
        var window = _dialogService.CreateDialog<BankAccountEditDialog>();
        var vm = (BankAccountEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAllAsync();
    }

    private async Task EditBankAccountAsync()
    {
        if (SelectedBankAccount is null) return;

        var window = _dialogService.CreateDialog<BankAccountEditDialog>();
        var vm = (BankAccountEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedBankAccount.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadAllAsync();
    }
}
