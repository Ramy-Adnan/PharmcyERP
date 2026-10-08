using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Sales;

/// <summary>A single sold line shown in the invoice-detail/return dialog, with an editable "quantity to return" field.</summary>
public class ReturnableLineRow : ViewModelBase
{
    private int _returnQuantity;

    public int SalesInvoiceItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string UnitName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    public int QuantityReturned { get; init; }
    public int QuantityReturnable { get; init; }

    public int ReturnQuantity { get => _returnQuantity; set => SetProperty(ref _returnQuantity, value); }
}

/// <summary>
/// ViewModel for the invoice detail dialog: shows all sold lines, lets an
/// authorized user process a customer return (fully or partially, per line)
/// or void the whole invoice if nothing has been returned from it yet.
/// </summary>
public class SalesInvoiceDetailViewModel : ViewModelBase
{
    private readonly ISalesService _salesService;
    private readonly IReceiptPrinter _printer;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private SalesInvoiceDto? _header;
    private string _returnReason = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public SalesInvoiceDetailViewModel(ISalesService salesService, IDialogService dialogService, ICurrentUserService currentUserService, IReceiptPrinter printer)
    {
        _salesService = salesService;
        _printer = printer;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Lines = new ObservableCollection<ReturnableLineRow>();

        PrintCommand = new AsyncRelayCommand(PrintAsync, () => !IsBusy && Header is not null);
        ProcessReturnCommand = new AsyncRelayCommand(ProcessReturnAsync, () => !IsBusy && CanProcessReturns);
        VoidInvoiceCommand = new AsyncRelayCommand(VoidInvoiceAsync, () => !IsBusy && CanVoid);
    }

    public bool CanProcessReturns => _currentUserService.HasPermission("Sales.ProcessReturns");
    public bool CanVoid => _currentUserService.HasPermission("Sales.VoidInvoices") && Header?.Status == SalesInvoiceStatus.Completed;

    public SalesInvoiceDto? Header { get => _header; private set => SetProperty(ref _header, value); }
    public ObservableCollection<ReturnableLineRow> Lines { get; }

    public string ReturnReason { get => _returnReason; set => SetProperty(ref _returnReason, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand PrintCommand { get; }
    private async Task PrintAsync()
    {
        if (Header is null) return;
        IsBusy = true;
        try
        {
            var invoice = await _salesService.GetSalesInvoiceDetailAsync(Header.Id);
            if (invoice is not null) _printer.Print(invoice);
        }
        catch (Exception ex) { ErrorMessage = $"تعذرت الطباعة: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public AsyncRelayCommand ProcessReturnCommand { get; }
    public AsyncRelayCommand VoidInvoiceCommand { get; }

    public async Task LoadAsync(int salesInvoiceId)
    {
        var detail = await _salesService.GetSalesInvoiceDetailAsync(salesInvoiceId);
        if (detail is null) return;

        Header = detail.Header;
        Lines.Clear();
        foreach (var line in detail.Lines)
        {
            Lines.Add(new ReturnableLineRow
            {
                SalesInvoiceItemId = line.Id,
                ItemName = line.ItemName,
                UnitName = line.UnitName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal,
                QuantityReturned = line.QuantityReturned,
                QuantityReturnable = line.QuantityReturnable
            });
        }

        OnPropertyChanged(nameof(CanVoid));
    }

    private async Task ProcessReturnAsync()
    {
        ErrorMessage = string.Empty;

        var linesToReturn = Lines.Where(l => l.ReturnQuantity > 0).ToList();
        if (!linesToReturn.Any()) { ErrorMessage = "الرجاء تحديد كمية إرجاع لصنف واحد على الأقل."; return; }
        if (string.IsNullOrWhiteSpace(ReturnReason)) { ErrorMessage = "سبب الإرجاع مطلوب."; return; }
        if (linesToReturn.Any(l => l.ReturnQuantity > l.QuantityReturnable))
        {
            ErrorMessage = "لا يمكن إرجاع كمية أكبر من الكمية القابلة للإرجاع لأحد الأصناف.";
            return;
        }

        var userId = _currentUserService.UserId;
        if (userId is null) { ErrorMessage = "تعذر تحديد المستخدم الحالي."; return; }

        IsBusy = true;
        try
        {
            var dto = new SalesReturnRequestDto
            {
                SalesInvoiceId = Header!.Id,
                Reason = ReturnReason.Trim(),
                Lines = linesToReturn.Select(l => new SalesReturnLineInputDto
                {
                    SalesInvoiceItemId = l.SalesInvoiceItemId,
                    Quantity = l.ReturnQuantity
                }).ToList()
            };

            var result = await _salesService.ProcessReturnAsync(dto, userId.Value);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر معالجة عملية الإرجاع.";
                return;
            }

            _dialogService.ShowInfo($"تم تسجيل مرتجع رقم {result.Value!.Number} بنجاح.");
            await LoadAsync(Header.Id);
            ReturnReason = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task VoidInvoiceAsync()
    {
        if (Header is null) return;
        if (!_dialogService.Confirm($"هل أنت متأكد من إلغاء الفاتورة '{Header.Number}'؟ سيتم إرجاع كامل الكمية إلى المخزون.")) return;

        IsBusy = true;
        try
        {
            var result = await _salesService.CancelSalesInvoiceAsync(Header.Id, _currentUserService.UserId);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر إلغاء الفاتورة.";
                return;
            }

            await LoadAsync(Header.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
