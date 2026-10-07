using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Accounting;

/// <summary>
/// ViewModel for creating a manual Journal Entry — used for adjustments that
/// don't originate from Sales/Purchasing/Insurance (which post automatically).
/// Enforces the fundamental double-entry rule client-side (total debits must
/// equal total credits) before allowing Save, mirroring the server-side check.
/// </summary>
public class ManualJournalEntryViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;
    private readonly IBranchService _branchService;

    private int _branchId;
    private DateTime _entryDate = DateTime.Today;
    private string _description = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private JournalEntryLineRow? _selectedLine;

    public ManualJournalEntryViewModel(IAccountingService accountingService, IBranchService branchService)
    {
        _accountingService = accountingService;
        _branchService = branchService;

        Branches = new ObservableCollection<BranchDto>();
        AvailableAccounts = new ObservableCollection<ChartOfAccountDto>();
        Lines = new ObservableCollection<JournalEntryLineRow>();

        AddLineCommand = new RelayCommand(() => Lines.Add(new JournalEntryLineRow()));
        RemoveLineCommand = new RelayCommand(() => { if (SelectedLine is not null) Lines.Remove(SelectedLine); }, () => SelectedLine is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);

        Lines.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalDebit));
            OnPropertyChanged(nameof(TotalCredit));
            OnPropertyChanged(nameof(IsBalanced));
        };
    }

    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<ChartOfAccountDto> AvailableAccounts { get; }
    public ObservableCollection<JournalEntryLineRow> Lines { get; }

    public int BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public DateTime EntryDate { get => _entryDate; set => SetProperty(ref _entryDate, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }

    public JournalEntryLineRow? SelectedLine
    {
        get => _selectedLine;
        set { if (SetProperty(ref _selectedLine, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>Recomputed on every line edit via the DataGrid's cell-edit-ended handler in the View's code-behind, which calls RecalculateTotals.</summary>
    public decimal TotalDebit => Lines.Sum(l => l.DebitAmount);
    public decimal TotalCredit => Lines.Sum(l => l.CreditAmount);
    public bool IsBalanced => Lines.Count >= 2 && TotalDebit == TotalCredit && TotalDebit > 0;

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadAsync()
    {
        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);
        if (BranchId == 0 && Branches.Count > 0) BranchId = Branches.First().Id;

        var accounts = await _accountingService.GetAccountsAsync(includeInactive: false);
        AvailableAccounts.Clear();
        foreach (var a in accounts) AvailableAccounts.Add(a);

        Lines.Add(new JournalEntryLineRow());
        Lines.Add(new JournalEntryLineRow());
    }

    /// <summary>Called from the View's code-behind whenever any line's amount changes, since ObservableCollection doesn't observe item-level PropertyChanged automatically.</summary>
    public void RecalculateTotals()
    {
        OnPropertyChanged(nameof(TotalDebit));
        OnPropertyChanged(nameof(TotalCredit));
        OnPropertyChanged(nameof(IsBalanced));
    }

    public void ApplyAccountSelection(JournalEntryLineRow line, int accountId)
    {
        var account = AvailableAccounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null) return;

        line.AccountId = account.Id;
        line.AccountName = account.Name;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (BranchId <= 0) { ErrorMessage = "الرجاء اختيار الفرع."; return; }
        if (string.IsNullOrWhiteSpace(Description)) { ErrorMessage = "وصف القيد مطلوب."; return; }
        if (Lines.Any(l => l.AccountId <= 0)) { ErrorMessage = "الرجاء اختيار الحساب لكل سطر."; return; }
        if (Lines.Any(l => l.DebitAmount == 0 && l.CreditAmount == 0)) { ErrorMessage = "كل سطر يجب أن يحتوي على مبلغ مدين أو دائن."; return; }
        if (!IsBalanced) { ErrorMessage = $"القيد غير متوازن. إجمالي المدين {TotalDebit:N2} ≠ إجمالي الدائن {TotalCredit:N2}."; return; }

        IsBusy = true;
        try
        {
            var dto = new ManualJournalEntryUpsertDto
            {
                BranchId = BranchId,
                EntryDate = EntryDate,
                Description = Description,
                Lines = Lines.Select(l => new JournalEntryLineInputDto
                {
                    AccountId = l.AccountId,
                    DebitAmount = l.DebitAmount,
                    CreditAmount = l.CreditAmount,
                    Description = l.Description
                }).ToList()
            };

            var result = await _accountingService.CreateManualEntryAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ القيد.";
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
