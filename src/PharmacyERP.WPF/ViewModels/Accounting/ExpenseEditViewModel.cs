using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Accounting;

/// <summary>ViewModel for recording a new Expense (rent, utilities, supplies, etc.) paid from a specific cash box or bank account.</summary>
public class ExpenseEditViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;
    private readonly IBranchService _branchService;

    private int _branchId;
    private int _expenseCategoryId;
    private DateTime _expenseDate = DateTime.Today;
    private decimal _amount;
    private string? _description;
    private CashSourceType _sourceType = CashSourceType.Cash;
    private int? _cashBoxId;
    private int? _bankAccountId;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public ExpenseEditViewModel(IAccountingService accountingService, IBranchService branchService)
    {
        _accountingService = accountingService;
        _branchService = branchService;

        Branches = new ObservableCollection<BranchDto>();
        Categories = new ObservableCollection<ExpenseCategoryDto>();
        CashBoxes = new ObservableCollection<CashBoxDto>();
        BankAccounts = new ObservableCollection<BankAccountDto>();
        SourceTypes = new ObservableCollection<CashSourceType>(Enum.GetValues<CashSourceType>());

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public string DialogTitle => "تسجيل مصروف جديد";

    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<ExpenseCategoryDto> Categories { get; }
    public ObservableCollection<CashBoxDto> CashBoxes { get; }
    public ObservableCollection<BankAccountDto> BankAccounts { get; }
    public ObservableCollection<CashSourceType> SourceTypes { get; }

    public int BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public int ExpenseCategoryId { get => _expenseCategoryId; set => SetProperty(ref _expenseCategoryId, value); }
    public DateTime ExpenseDate { get => _expenseDate; set => SetProperty(ref _expenseDate, value); }
    public decimal Amount { get => _amount; set => SetProperty(ref _amount, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }

    public CashSourceType SourceType
    {
        get => _sourceType;
        set
        {
            if (SetProperty(ref _sourceType, value))
            {
                OnPropertyChanged(nameof(IsCashSource));
                OnPropertyChanged(nameof(IsBankSource));
            }
        }
    }

    public bool IsCashSource => SourceType == CashSourceType.Cash;
    public bool IsBankSource => SourceType == CashSourceType.Bank;

    public int? CashBoxId { get => _cashBoxId; set => SetProperty(ref _cashBoxId, value); }
    public int? BankAccountId { get => _bankAccountId; set => SetProperty(ref _bankAccountId, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadAsync()
    {
        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);

        var categories = await _accountingService.GetExpenseCategoriesAsync();
        Categories.Clear();
        foreach (var c in categories.Where(c => c.IsActive)) Categories.Add(c);

        var cashBoxes = await _accountingService.GetCashBoxesAsync();
        CashBoxes.Clear();
        foreach (var c in cashBoxes.Where(c => c.IsActive)) CashBoxes.Add(c);

        var bankAccounts = await _accountingService.GetBankAccountsAsync();
        BankAccounts.Clear();
        foreach (var b in bankAccounts.Where(b => b.IsActive)) BankAccounts.Add(b);

        if (BranchId == 0 && Branches.Count > 0) BranchId = Branches.First().Id;
        if (ExpenseCategoryId == 0 && Categories.Count > 0) ExpenseCategoryId = Categories.First().Id;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (BranchId <= 0) { ErrorMessage = "الرجاء اختيار الفرع."; return; }
        if (ExpenseCategoryId <= 0) { ErrorMessage = "الرجاء اختيار فئة المصروف."; return; }
        if (Amount <= 0) { ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر."; return; }
        if (SourceType == CashSourceType.Cash && CashBoxId is null) { ErrorMessage = "الرجاء اختيار الصندوق النقدي."; return; }
        if (SourceType == CashSourceType.Bank && BankAccountId is null) { ErrorMessage = "الرجاء اختيار الحساب البنكي."; return; }

        IsBusy = true;
        try
        {
            var dto = new ExpenseCreateDto
            {
                BranchId = BranchId,
                ExpenseCategoryId = ExpenseCategoryId,
                ExpenseDate = ExpenseDate,
                Amount = Amount,
                Description = Description,
                SourceType = SourceType,
                CashBoxId = SourceType == CashSourceType.Cash ? CashBoxId : null,
                BankAccountId = SourceType == CashSourceType.Bank ? BankAccountId : null
            };

            var result = await _accountingService.CreateExpenseAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تسجيل المصروف.";
                return;
            }

            SavedSuccessfully = true;
            RequestClose?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
