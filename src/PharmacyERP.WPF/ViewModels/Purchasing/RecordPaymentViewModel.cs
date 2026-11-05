using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

public class RecordPaymentViewModel : ViewModelBase
{
    private readonly IPurchasingService _purchasingService;

    private int _purchaseInvoiceId;
    private string _invoiceNumber = string.Empty;
    private decimal _amountDue;
    private decimal _amount;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private RecordPaymentDto? _pendingPayment;

    public RecordPaymentViewModel(IPurchasingService purchasingService)
    {
        _purchasingService = purchasingService;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public string InvoiceNumber { get => _invoiceNumber; set => SetProperty(ref _invoiceNumber, value); }
    public decimal AmountDue { get => _amountDue; set => SetProperty(ref _amountDue, value); }
    public decimal Amount { get => _amount; set { if (!HasPendingPayment) SetProperty(ref _amount, value); } }
    public string? Notes { get => _notes; set { if (!HasPendingPayment) SetProperty(ref _notes, value); } }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) NotifyPaymentState(); } }
    public bool HasPendingPayment => _pendingPayment is not null;
    public bool CanEditPayment => !IsBusy && !HasPendingPayment;
    public string SaveLabel => HasPendingPayment ? "التحقق من الدفعة السابقة" : "حفظ";

    private void NotifyPaymentState()
    {
        OnPropertyChanged(nameof(HasPendingPayment)); OnPropertyChanged(nameof(CanEditPayment)); OnPropertyChanged(nameof(SaveLabel));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public void Load(int purchaseInvoiceId, string invoiceNumber, decimal amountDue)
    {
        if (IsBusy || HasPendingPayment) return;
        SavedSuccessfully = false; ErrorMessage = string.Empty;
        _purchaseInvoiceId = purchaseInvoiceId;
        InvoiceNumber = invoiceNumber;
        AmountDue = amountDue;
        Amount = amountDue;
    }

    public async Task SaveAsync()
    {
        if (IsBusy || SavedSuccessfully) return;
        ErrorMessage = string.Empty;
        if (_pendingPayment is null)
        {
            if (Amount <= 0 || Amount > AmountDue) { ErrorMessage = "أدخل مبلغاً موجباً لا يتجاوز المتبقي."; return; }
            _pendingPayment = new RecordPaymentDto { PurchaseInvoiceId = _purchaseInvoiceId, Amount = Amount, Notes = Notes };
        }
        IsBusy = true;
        try
        {
            var result = await _purchasingService.RecordPaymentAsync(_pendingPayment);

            if (!result.Succeeded)
            {
                _pendingPayment = null;
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تسجيل الدفعة.";
                return;
            }

            _pendingPayment = null;
            SavedSuccessfully = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = "تعذر التأكد من تسجيل الدفعة. أعد الاتصال واضغط التحقق من الدفعة السابقة قبل تسجيل مبلغ آخر. " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
        if (SavedSuccessfully) RequestClose?.Invoke();
    }
}
