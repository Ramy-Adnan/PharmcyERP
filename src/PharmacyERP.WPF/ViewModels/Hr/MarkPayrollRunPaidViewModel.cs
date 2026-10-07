using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Hr;

/// <summary>ViewModel for the small dialog that finalizes payment of an approved Payroll Run, choosing which cash box or bank account the money actually left from — this is the one moment HR posts to Accounting.</summary>
public class MarkPayrollRunPaidViewModel : ViewModelBase
{
    private readonly IHrService _hrService;
    private readonly IAccountingService _accountingService;

    private int _payrollRunId;
    private string _payrollRunNumber = string.Empty;
    private decimal _totalNetPay;
    private DateTime _paymentDate = DateTime.Today;
    private CashSourceType _sourceType = CashSourceType.Cash;
    private int? _cashBoxId;
    private int? _bankAccountId;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public MarkPayrollRunPaidViewModel(IHrService hrService, IAccountingService accountingService)
    {
        _hrService = hrService;
        _accountingService = accountingService;

        CashBoxes = new ObservableCollection<CashBoxDto>();
        BankAccounts = new ObservableCollection<BankAccountDto>();
        SourceTypes = new ObservableCollection<CashSourceType>(Enum.GetValues<CashSourceType>());

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public string PayrollRunNumber { get => _payrollRunNumber; private set => SetProperty(ref _payrollRunNumber, value); }
    public decimal TotalNetPay { get => _totalNetPay; private set => SetProperty(ref _totalNetPay, value); }

    public ObservableCollection<CashBoxDto> CashBoxes { get; }
    public ObservableCollection<BankAccountDto> BankAccounts { get; }
    public ObservableCollection<CashSourceType> SourceTypes { get; }

    public DateTime PaymentDate { get => _paymentDate; set => SetProperty(ref _paymentDate, value); }

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

    public async Task LoadAsync(int payrollRunId, string payrollRunNumber, decimal totalNetPay)
    {
        _payrollRunId = payrollRunId;
        PayrollRunNumber = payrollRunNumber;
        TotalNetPay = totalNetPay;

        var cashBoxes = await _accountingService.GetCashBoxesAsync();
        CashBoxes.Clear();
        foreach (var c in cashBoxes.Where(c => c.IsActive)) CashBoxes.Add(c);

        var bankAccounts = await _accountingService.GetBankAccountsAsync();
        BankAccounts.Clear();
        foreach (var b in bankAccounts.Where(b => b.IsActive)) BankAccounts.Add(b);
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (SourceType == CashSourceType.Cash && CashBoxId is null) { ErrorMessage = "الرجاء اختيار الصندوق النقدي."; return; }
        if (SourceType == CashSourceType.Bank && BankAccountId is null) { ErrorMessage = "الرجاء اختيار الحساب البنكي."; return; }

        IsBusy = true;
        try
        {
            var dto = new MarkPayrollRunPaidDto
            {
                PayrollRunId = _payrollRunId,
                PaymentDate = PaymentDate,
                SourceType = SourceType,
                CashBoxId = SourceType == CashSourceType.Cash ? CashBoxId : null,
                BankAccountId = SourceType == CashSourceType.Bank ? BankAccountId : null
            };

            var result = await _hrService.MarkPayrollRunPaidAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تسجيل صرف الرواتب.";
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
