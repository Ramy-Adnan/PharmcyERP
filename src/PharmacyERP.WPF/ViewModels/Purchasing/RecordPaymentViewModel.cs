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

    public RecordPaymentViewModel(IPurchasingService purchasingService)
    {
        _purchasingService = purchasingService;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public string InvoiceNumber { get => _invoiceNumber; set => SetProperty(ref _invoiceNumber, value); }
    public decimal AmountDue { get => _amountDue; set => SetProperty(ref _amountDue, value); }
    public decimal Amount { get => _amount; set => SetProperty(ref _amount, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public void Load(int purchaseInvoiceId, string invoiceNumber, decimal amountDue)
    {
        _purchaseInvoiceId = purchaseInvoiceId;
        InvoiceNumber = invoiceNumber;
        AmountDue = amountDue;
        Amount = amountDue;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _purchasingService.RecordPaymentAsync(new RecordPaymentDto
            {
                PurchaseInvoiceId = _purchaseInvoiceId,
                Amount = Amount,
                Notes = Notes
            });

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تسجيل الدفعة.";
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
