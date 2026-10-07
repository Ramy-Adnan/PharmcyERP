using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Sales;

public class CustomerAccountViewModel : ViewModelBase
{
    private readonly ISalesService _sales;
    private readonly IDialogService _dialogs;
    private readonly ICurrentUserService _user;
    private int _customerId;
    private Guid _requestId = Guid.NewGuid();
    private CustomerCreditInvoiceDto? _selectedInvoice;
    private decimal _paymentAmount;
    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private string _customerName = "", _message = "";
    private bool _isBusy;
    public CustomerAccountViewModel(ISalesService sales, IDialogService dialogs, ICurrentUserService user)
    {
        _sales = sales; _dialogs = dialogs; _user = user;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        PayCommand = new AsyncRelayCommand(PayAsync, () => CanManage && !IsBusy && SelectedInvoice?.OutstandingAmount > 0);
        ReconcileCommand = new AsyncRelayCommand(ReconcileAsync, () => CanManage && !IsBusy);
    }
    public ObservableCollection<CustomerCreditInvoiceDto> Invoices { get; } = new();
    public ObservableCollection<CustomerPaymentDto> Payments { get; } = new();
    public IReadOnlyList<PaymentOption> PaymentMethods { get; } = new[] { new PaymentOption(PaymentMethod.Cash, "نقدي"), new PaymentOption(PaymentMethod.Card, "بطاقة") };
    public bool CanManage => _user.HasPermission("Sales.ManageCustomers");
    public string CustomerName { get => _customerName; private set => SetProperty(ref _customerName, value); }
    public string Message { get => _message; set => SetProperty(ref _message, value); }
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public decimal OutstandingAmount => Invoices.Sum(i => i.OutstandingAmount);
    public CustomerCreditInvoiceDto? SelectedInvoice
    {
        get => _selectedInvoice;
        set { if (SetProperty(ref _selectedInvoice, value)) PaymentAmount = value?.OutstandingAmount ?? 0; System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }
    public decimal PaymentAmount { get => _paymentAmount; set => SetProperty(ref _paymentAmount, value); }
    public PaymentMethod PaymentMethod { get => _paymentMethod; set => SetProperty(ref _paymentMethod, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand PayCommand { get; }
    public AsyncRelayCommand ReconcileCommand { get; }
    public async Task LoadAsync(int customerId) { _customerId = customerId; await RefreshAsync(); }
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try { await LoadAccountAsync(); }
        catch (Exception ex) { Message = $"تعذر تحميل الكشف: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    private async Task LoadAccountAsync()
    {
        var selectedId = SelectedInvoice?.InvoiceId;
        var account = await _sales.GetCustomerAccountAsync(_customerId);
        if (account is null) { Message = "العميل غير موجود."; return; }
        CustomerName = account.CustomerName;
        Invoices.Clear(); foreach (var i in account.Invoices) Invoices.Add(i);
        Payments.Clear(); foreach (var p in account.Payments) Payments.Add(p);
        if (Payments.Any(p => p.Number == "RCT-" + _requestId.ToString("N")[..24])) _requestId = Guid.NewGuid();
        SelectedInvoice = Invoices.FirstOrDefault(i => i.InvoiceId == selectedId);
        OnPropertyChanged(nameof(OutstandingAmount));
    }
    private async Task PayAsync()
    {
        if (SelectedInvoice is null || PaymentAmount <= 0 || PaymentAmount > SelectedInvoice.OutstandingAmount)
        { Message = "أدخل مبلغاً موجباً لا يتجاوز الدين المتبقي."; return; }
        if (!_dialogs.Confirm($"تأكيد قبض {PaymentAmount:N2} من {CustomerName} لتسديد الفاتورة {SelectedInvoice.Number}؟")) return;
        IsBusy = true;
        try
        {
            var result = await _sales.RecordCustomerPaymentAsync(new CustomerDebtPaymentDto
            { RequestId = _requestId, CustomerId = _customerId, InvoiceId = SelectedInvoice.InvoiceId, Amount = PaymentAmount, PaymentMethod = PaymentMethod });
            if (!result.Succeeded) { Message = result.Errors.FirstOrDefault() ?? "تعذر التسديد."; return; }
            _requestId = Guid.NewGuid();
            Message = "تم تسجيل القبض وتخفيض الدين؛ لم تُسجل مبيعات جديدة.";
            await LoadAccountAsync();
        }
        catch (Exception ex) { Message = $"تعذر التسديد: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    private async Task ReconcileAsync()
    {
        if (!_dialogs.Confirm("سيُضاف قيد تصحيح لنقل الفواتير الآجلة القديمة التي رحلت نقداً إلى ذمم العملاء، دون حذف القيود الأصلية. تأكد من مطابقة أي تسديدات قديمة خارج النظام. هل تريد المتابعة؟")) return;
        IsBusy = true;
        try
        {
            var result = await _sales.ReconcileCustomerCreditAsync(_customerId);
            Message = result.Succeeded ? "اكتمل فحص وتصحيح التصنيف المحاسبي للفواتير الآجلة القديمة." : result.Errors.FirstOrDefault() ?? "تعذر التصحيح.";
            await LoadAccountAsync();
        }
        catch (Exception ex) { Message = $"تعذر التصحيح: {ex.Message}"; }
        finally { IsBusy = false; }
    }
}
