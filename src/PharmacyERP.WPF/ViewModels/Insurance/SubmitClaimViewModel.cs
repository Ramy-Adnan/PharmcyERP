using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Insurance;
using PharmacyERP.Application.Features.Insurance.DTOs;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Insurance;

/// <summary>
/// ViewModel for submitting a new insurance claim against an already-completed
/// sale. The cashier already collected full payment at POS — this is a
/// back-office reimbursement request, so the flow is: pick the invoice, pick
/// the customer's active policy, and the claimed amount defaults to the
/// policy's coverage percentage of the invoice total (editable).
/// </summary>
public class SubmitClaimViewModel : ViewModelBase
{
    private readonly IInsuranceService _insuranceService;
    private readonly ISalesService _salesService;

    private SalesInvoiceDto? _selectedInvoice;
    private InsurancePolicyDto? _selectedPolicy;
    private decimal _claimedAmount;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public SubmitClaimViewModel(IInsuranceService insuranceService, ISalesService salesService)
    {
        _insuranceService = insuranceService;
        _salesService = salesService;

        Invoices = new ObservableCollection<SalesInvoiceDto>();
        CustomerPolicies = new ObservableCollection<InsurancePolicyDto>();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public ObservableCollection<SalesInvoiceDto> Invoices { get; }
    public ObservableCollection<InsurancePolicyDto> CustomerPolicies { get; }

    public SalesInvoiceDto? SelectedInvoice
    {
        get => _selectedInvoice;
        set
        {
            if (SetProperty(ref _selectedInvoice, value))
                _ = LoadPoliciesForSelectedInvoiceAsync();
        }
    }

    public InsurancePolicyDto? SelectedPolicy
    {
        get => _selectedPolicy;
        set
        {
            if (SetProperty(ref _selectedPolicy, value) && value is not null && SelectedInvoice is not null)
                ClaimedAmount = Math.Round(SelectedInvoice.TotalAmount * value.CoveragePercent / 100m, 2);
        }
    }

    public decimal ClaimedAmount { get => _claimedAmount; set => SetProperty(ref _claimedAmount, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadAsync()
    {
        var invoices = await _salesService.GetSalesInvoicesAsync();
        Invoices.Clear();
        foreach (var i in invoices.Where(i => i.CustomerId.HasValue)) Invoices.Add(i);
    }

    private async Task LoadPoliciesForSelectedInvoiceAsync()
    {
        CustomerPolicies.Clear();
        if (SelectedInvoice?.CustomerId is null) return;

        var policies = await _insuranceService.GetActivePoliciesForCustomerAsync(SelectedInvoice.CustomerId.Value);
        foreach (var p in policies) CustomerPolicies.Add(p);
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (SelectedInvoice is null) { ErrorMessage = "الرجاء اختيار الفاتورة."; return; }
        if (SelectedPolicy is null) { ErrorMessage = "الرجاء اختيار بوليصة التأمين."; return; }
        if (ClaimedAmount <= 0) { ErrorMessage = "مبلغ المطالبة يجب أن يكون أكبر من صفر."; return; }

        IsBusy = true;
        try
        {
            var dto = new SubmitClaimDto
            {
                SalesInvoiceId = SelectedInvoice.Id,
                InsurancePolicyId = SelectedPolicy.Id,
                ClaimedAmount = ClaimedAmount,
                Notes = Notes
            };

            var result = await _insuranceService.SubmitClaimAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تقديم المطالبة.";
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
