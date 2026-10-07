using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Accounting;

namespace PharmacyERP.WPF.ViewModels.Accounting;

public class ExpensesViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private bool _isBusy;

    public ExpensesViewModel(IAccountingService accountingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _accountingService = accountingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Expenses = new ObservableCollection<ExpenseDto>();

        RefreshCommand = new AsyncRelayCommand(LoadExpensesAsync);
        AddExpenseCommand = new AsyncRelayCommand(AddExpenseAsync, () => CanManage);
    }

    public bool CanManage => _currentUserService.HasPermission("Accounting.ManageExpenses");

    public ObservableCollection<ExpenseDto> Expenses { get; }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddExpenseCommand { get; }

    public async Task InitializeAsync() => await LoadExpensesAsync();

    private async Task LoadExpensesAsync()
    {
        IsBusy = true;
        try
        {
            var expenses = await _accountingService.GetExpensesAsync();
            Expenses.Clear();
            foreach (var e in expenses) Expenses.Add(e);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddExpenseAsync()
    {
        var window = _dialogService.CreateDialog<ExpenseEditDialog>();
        var vm = (ExpenseEditViewModel)window.DataContext;
        await vm.LoadAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadExpensesAsync();
    }
}
