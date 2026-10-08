using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

public enum PurchaseStep { Supplier, Goods, Invoice, Payment }

/// <summary>A guided purchase keeps the same saved receipt/invoice when continuing or reopening a document.</summary>
public class PurchasingWorkspaceViewModel : ViewModelBase
{
    private readonly IPurchasingService _purchasing;
    private readonly IInventoryService _inventory;
    private readonly IBranchService _branches;
    private readonly ICurrentUserService _user;
    private PurchaseStep _step;
    private int _supplierId;
    private bool _isBusy, _initialized, _receiptPosted, _invoicePrepared;
    private string _message = string.Empty, _error = string.Empty;
    private decimal _paymentAmount;
    private GoodsReceiptNoteDto? _resumeReceipt;
    private PurchaseInvoiceDto? _resumeInvoice, _invoice;
    private GoodsReceiptEditViewModel _receipt = null!;
    private PurchaseInvoiceEditViewModel _invoiceEditor = null!;
    private SupplierEditViewModel _supplierEditor = null!;

    public PurchasingWorkspaceViewModel(IPurchasingService purchasing, IInventoryService inventory,
        IBranchService branches, ICurrentUserService user)
    {
        _purchasing = purchasing; _inventory = inventory; _branches = branches; _user = user;
        CreateEditors();
        NextCommand = new AsyncRelayCommand(ContinueAsync, () => CanContinue);
        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, () => !IsBusy && IsGoodsStep && CanReceiveGoods && !_receiptPosted);
        BackCommand = new RelayCommand(GoBack, () => !IsBusy && IsGoodsStep && !_receiptPosted);
        NewPurchaseCommand = new AsyncRelayCommand(StartNewAsync, () => !IsBusy && (IsSupplierStep || IsPaymentStep));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SaveSupplierCommand = new AsyncRelayCommand(SaveSupplierAsync, () => !IsBusy && CanManageSuppliers && CanChangeSupplier);
        ResumeReceiptCommand = new AsyncRelayCommand(() => ResumeReceiptAsync(SelectedReceiptToResume!.Id),
            () => !IsBusy && SelectedReceiptToResume is not null && CanReceiveGoods);
        ResumeInvoiceCommand = new AsyncRelayCommand(() => ResumeInvoiceAsync(SelectedInvoiceToResume!.Id),
            () => !IsBusy && SelectedInvoiceToResume is not null && CanManageInvoices);
        PayCommand = new AsyncRelayCommand(PayAsync, () => !IsBusy && IsPaymentStep && CanManageInvoices && Invoice?.AmountDue > 0);
        PayInFullCommand = new RelayCommand(() => PaymentAmount = Invoice?.AmountDue ?? 0,
            () => !IsBusy && IsPaymentStep && Invoice?.AmountDue > 0);
    }

    public bool CanManageSuppliers => _user.HasPermission("Purchasing.ManageSuppliers");
    public bool CanManageOrders => _user.HasPermission("Purchasing.ManageOrders");
    public bool CanManageItems => _user.HasPermission("Inventory.ManageItems");
    public bool CanReceiveGoods => _user.HasPermission("Purchasing.ReceiveGoods");
    public bool CanManageInvoices => _user.HasPermission("Purchasing.ManageInvoices");
    public ObservableCollection<SupplierDto> Suppliers { get; } = new();
    public ObservableCollection<GoodsReceiptNoteDto> ReceiptsToResume { get; } = new();
    public ObservableCollection<PurchaseInvoiceDto> InvoicesToResume { get; } = new();
    public GoodsReceiptEditViewModel Receipt { get => _receipt; private set => SetProperty(ref _receipt, value); }
    public PurchaseInvoiceEditViewModel InvoiceEditor { get => _invoiceEditor; private set => SetProperty(ref _invoiceEditor, value); }
    public SupplierEditViewModel SupplierEditor { get => _supplierEditor; private set => SetProperty(ref _supplierEditor, value); }
    public int? ReceiptId => Receipt.SavedReceiptId;
    public bool CanChangeSupplier => !ReceiptId.HasValue && !_receiptPosted;
    public bool CanAddSupplier => CanManageSuppliers && CanChangeSupplier;
    public int SelectedSupplierId
    {
        get => _supplierId;
        set { if (CanChangeSupplier && SetProperty(ref _supplierId, value)) NotifyState(); }
    }
    public SupplierDto? SelectedSupplier => Suppliers.FirstOrDefault(s => s.Id == _supplierId);
    public GoodsReceiptNoteDto? SelectedReceiptToResume
    {
        get => _resumeReceipt;
        set { if (SetProperty(ref _resumeReceipt, value)) NotifyState(); }
    }
    public PurchaseInvoiceDto? SelectedInvoiceToResume
    {
        get => _resumeInvoice;
        set { if (SetProperty(ref _resumeInvoice, value)) NotifyState(); }
    }
    public PurchaseInvoiceDto? Invoice
    {
        get => _invoice;
        private set { if (SetProperty(ref _invoice, value)) NotifyState(); }
    }
    public PurchaseStep Step { get => _step; private set { if (SetProperty(ref _step, value)) NotifyState(); } }
    public bool IsSupplierStep => Step == PurchaseStep.Supplier;
    public bool IsGoodsStep => Step == PurchaseStep.Goods;
    public bool IsInvoiceStep => Step == PurchaseStep.Invoice;
    public bool IsPaymentStep => Step == PurchaseStep.Payment;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) NotifyState(); } }
    public bool CanInteract => !IsBusy;
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string ErrorMessage { get => _error; private set => SetProperty(ref _error, value); }
    public decimal PaymentAmount { get => _paymentAmount; set => SetProperty(ref _paymentAmount, value); }
    public string ReceiptLabel { get; private set; } = "لم يُحفظ الاستلام بعد";
    public bool CanContinue => !IsBusy && (Step switch
    {
        PurchaseStep.Supplier => CanReceiveGoods && SelectedSupplier?.IsActive == true,
        PurchaseStep.Goods => CanReceiveGoods,
        PurchaseStep.Invoice => CanManageInvoices,
        _ => true
    });
    public string NextLabel => Step switch
    {
        PurchaseStep.Supplier => "التالي: إدخال البضاعة",
        PurchaseStep.Goods => "حفظ وإضافة للمخزن ← الفاتورة",
        PurchaseStep.Invoice => "حفظ الفاتورة ← التسديد",
        _ => "عملية شراء جديدة"
    };
    public string StepTitle => Step switch
    {
        PurchaseStep.Supplier => "1 · اختر المورد",
        PurchaseStep.Goods => "2 · أدخل البضاعة وأضفها للمخزن",
        PurchaseStep.Invoice => "3 · راجع فاتورة المورد",
        _ => "4 · سجّل المدفوع وتابع المتبقي"
    };
    public string StepHint => Step switch
    {
        PurchaseStep.Supplier when !CanChangeSupplier => "الاستلام محفوظ لهذا المورد. تابع إدخال البضاعة أو ابدأ عملية جديدة؛ تبقى المسودة السابقة في السجلات.",
        PurchaseStep.Supplier => "اختر شركة أو مورداً مسجلاً، أو أضف مورداً جديداً هنا. يمكنك أيضاً استكمال عملية محفوظة.",
        PurchaseStep.Goods => "أضف العلاجات والكمية ورقم الدفعة والصلاحية. اختر 20% لباي هاند أو 25% للباقي وراجع البيع. أمر الشراء اختياري.",
        PurchaseStep.Invoice when !CanManageInvoices => "تم إدخال البضاعة للمخزن. حفظ الفاتورة يحتاج مستخدماً لديه صلاحية إدارة فواتير الشراء.",
        PurchaseStep.Invoice => "الأصناف والتكلفة من الاستلام. راجع الضريبة والخصم وتاريخ الاستحقاق؛ نسبة البيع لا تُضاف إلى مبلغ المورد.",
        _ => "الفاتورة محفوظة. سجّل المبلغ المدفوع الآن، أو اترك المتبقي آجلاً ثم استكمل تسديده لاحقاً من هذه الشاشة."
    };
    public string PaymentStatus => Invoice is null ? "" : Invoice.AmountDue == 0 ? "مسددة بالكامل" : Invoice.AmountPaid > 0 ? "مسددة جزئياً" : "غير مسددة / آجل";
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand SaveDraftCommand { get; }
    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand NewPurchaseCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveSupplierCommand { get; }
    public AsyncRelayCommand ResumeReceiptCommand { get; }
    public AsyncRelayCommand ResumeInvoiceCommand { get; }
    public AsyncRelayCommand PayCommand { get; }
    public RelayCommand PayInFullCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        await RunAsync(async () => { await Receipt.LoadForCreateAsync(_user.CurrentBranchId); await LoadListsAsync(); _initialized = true; });
    }
    public Task RefreshAsync() => RunAsync(LoadListsAsync);
    public Task StartNewAsync() => RunAsync(StartNewCoreAsync);
    private async Task StartNewCoreAsync()
    {
        CreateEditors(); _receiptPosted = false; _invoicePrepared = false; _supplierId = 0; Invoice = null;
        ReceiptLabel = "لم يُحفظ الاستلام بعد"; PaymentAmount = 0; Step = PurchaseStep.Supplier;
        await Receipt.LoadForCreateAsync(_user.CurrentBranchId); await LoadListsAsync(); NotifyState();
    }
    private void CreateEditors()
    {
        Receipt = new GoodsReceiptEditViewModel(_purchasing, _inventory, _branches);
        InvoiceEditor = new PurchaseInvoiceEditViewModel(_purchasing, _inventory, _branches);
        SupplierEditor = new SupplierEditViewModel(_purchasing); SupplierEditor.LoadForCreate();
    }
    private async Task LoadListsAsync()
    {
        var supplierId = _supplierId;
        var suppliers = await _purchasing.GetSuppliersAsync(); Suppliers.Clear();
        foreach (var supplier in suppliers) Suppliers.Add(supplier);
        _supplierId = supplierId; OnPropertyChanged(nameof(SelectedSupplierId));
        var receipts = await _purchasing.GetGoodsReceiptNotesAsync(); ReceiptsToResume.Clear();
        foreach (var receipt in receipts.Where(r => r.Status != GoodsReceiptStatus.Cancelled)) ReceiptsToResume.Add(receipt);
        var invoices = await _purchasing.GetPurchaseInvoicesAsync(); InvoicesToResume.Clear();
        foreach (var invoice in invoices.Where(i => i.Status != PurchaseInvoiceStatus.Cancelled)) InvoicesToResume.Add(invoice);
        if (Invoice is not null) Invoice = invoices.FirstOrDefault(i => i.Id == Invoice.Id);
        NotifyState();
    }
    public Task SaveSupplierAsync() => RunAsync(async () =>
    {
        if (!CanAddSupplier) { ErrorMessage = "إضافة المورد تحتاج صلاحية إدارة الموردين."; return; }
        await SupplierEditor.SaveAsync();
        if (!SupplierEditor.SavedSuccessfully) { ErrorMessage = SupplierEditor.ErrorMessage; return; }
        await LoadListsAsync(); SelectedSupplierId = SupplierEditor.SavedSupplierId!.Value;
        SupplierEditor = new SupplierEditViewModel(_purchasing); SupplierEditor.LoadForCreate();
        Message = "تم حفظ المورد واختياره للعملية.";
    });
    public Task ContinueAsync() => RunAsync(async () =>
    {
        switch (Step)
        {
            case PurchaseStep.Supplier:
                if (!CanReceiveGoods) { ErrorMessage = "تحتاج صلاحية استلام البضاعة لبدء عملية شراء."; return; }
                if (SelectedSupplier?.IsActive != true) { ErrorMessage = "اختر مورداً نشطاً أولاً."; return; }
                await Receipt.SelectSupplierAsync(_supplierId); Step = PurchaseStep.Goods; break;
            case PurchaseStep.Goods:
                if (!CanReceiveGoods) { ErrorMessage = "تحتاج صلاحية استلام البضاعة."; return; }
                if (!await SaveReceiptCoreAsync()) return;
                var result = await _purchasing.PostGoodsReceiptAsync(ReceiptId!.Value, _user.UserId);
                if (!result.Succeeded) { ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر إضافة البضاعة للمخزن؛ المسودة محفوظة."; return; }
                _receiptPosted = true;
                // Leave the goods step immediately after posting, even if the following lookup fails.
                Step = PurchaseStep.Invoice;
                await PrepareInvoiceAsync(ReceiptId.Value);
                Message = "تم إدخال البضاعة للمخزن. راجع الفاتورة ثم احفظها."; break;
            case PurchaseStep.Invoice:
                if (!CanManageInvoices) { ErrorMessage = "تحتاج صلاحية إدارة فواتير الشراء."; return; }
                var existing = (await _purchasing.GetPurchaseInvoicesAsync()).FirstOrDefault(i => i.GoodsReceiptNoteId == ReceiptId && i.Status != PurchaseInvoiceStatus.Cancelled);
                if (existing is not null) { Invoice = existing; Step = PurchaseStep.Payment; break; }
                if (!_invoicePrepared || InvoiceEditor.GoodsReceiptNoteId != ReceiptId) await PrepareInvoiceAsync(ReceiptId!.Value);
                await InvoiceEditor.SaveAsync();
                if (!InvoiceEditor.SavedSuccessfully) { ErrorMessage = InvoiceEditor.ErrorMessage; return; }
                Invoice = InvoiceEditor.SavedInvoice; Step = PurchaseStep.Payment;
                Message = "تم حفظ الفاتورة. يمكنك تسديدها كاملة أو جزئياً أو إبقاء المبلغ آجلاً."; break;
            case PurchaseStep.Payment: await StartNewCoreAsync(); break;
        }
        NotifyState();
    });
    public Task SaveDraftAsync() => RunAsync(async () =>
    {
        if (!CanReceiveGoods || !IsGoodsStep || _receiptPosted) return;
        if (await SaveReceiptCoreAsync()) { await LoadListsAsync(); Message = "المسودة محفوظة؛ ستدخل الكمية للمخزن عند الضغط على حفظ وإضافة للمخزن."; }
    });
    private async Task<bool> SaveReceiptCoreAsync()
    {
        if (_receiptPosted) return true;
        await Receipt.SaveAsync();
        if (!Receipt.SavedSuccessfully) { ErrorMessage = Receipt.ErrorMessage; return false; }
        ReceiptLabel = (await _purchasing.GetGoodsReceiptNotesAsync()).First(r => r.Id == ReceiptId).Number;
        NotifyState(); return true;
    }
    private async Task PrepareInvoiceAsync(int receiptId)
    {
        _invoicePrepared = false;
        InvoiceEditor = new PurchaseInvoiceEditViewModel(_purchasing, _inventory, _branches);
        await InvoiceEditor.LoadForCreateAsync(); await InvoiceEditor.PreselectGoodsReceiptAsync(receiptId);
        if (SelectedSupplier?.PaymentTermsDays > 0)
            InvoiceEditor.DueDate = InvoiceEditor.InvoiceDate.AddDays(SelectedSupplier.PaymentTermsDays);
        _invoicePrepared = true;
    }
    public Task ResumeReceiptAsync(int receiptId) => RunAsync(async () =>
    {
        if (!CanReceiveGoods) { ErrorMessage = "تحتاج صلاحية استلام البضاعة."; return; }
        var note = (await _purchasing.GetGoodsReceiptNotesAsync()).FirstOrDefault(r => r.Id == receiptId && r.Status != GoodsReceiptStatus.Cancelled);
        if (note is null) { ErrorMessage = "سند الاستلام غير موجود أو ملغى."; return; }
        Receipt = new GoodsReceiptEditViewModel(_purchasing, _inventory, _branches); await Receipt.LoadForEditAsync(receiptId);
        _supplierId = Receipt.SupplierId; _receiptPosted = note.Status == GoodsReceiptStatus.Posted; Invoice = null; ReceiptLabel = note.Number;
        if (!_receiptPosted) Step = PurchaseStep.Goods;
        else
        {
            Invoice = (await _purchasing.GetPurchaseInvoicesAsync()).FirstOrDefault(i => i.GoodsReceiptNoteId == receiptId && i.Status != PurchaseInvoiceStatus.Cancelled);
            Step = Invoice is null ? PurchaseStep.Invoice : PurchaseStep.Payment;
            if (Invoice is null) await PrepareInvoiceAsync(receiptId);
        }
        PaymentAmount = 0; Message = "تم فتح العملية المحفوظة للمتابعة."; NotifyState();
    });
    public Task ResumeInvoiceAsync(int invoiceId) => RunAsync(async () =>
    {
        if (!CanManageInvoices) { ErrorMessage = "تحتاج صلاحية إدارة فواتير الشراء."; return; }
        var invoice = (await _purchasing.GetPurchaseInvoicesAsync()).FirstOrDefault(i => i.Id == invoiceId && i.Status != PurchaseInvoiceStatus.Cancelled);
        if (invoice is null) { ErrorMessage = "الفاتورة غير موجودة أو ملغاة."; return; }
        Invoice = invoice; _supplierId = invoice.SupplierId; Step = PurchaseStep.Payment;
        PaymentAmount = 0; ReceiptLabel = invoice.GoodsReceiptNumber ?? "فاتورة مباشرة"; NotifyState();
    });
    public Task PayAsync() => RunAsync(async () =>
    {
        if (!CanManageInvoices || !IsPaymentStep || Invoice is null) { ErrorMessage = "تحتاج فاتورة محفوظة وصلاحية إدارة فواتير الشراء."; return; }
        if (PaymentAmount <= 0 || PaymentAmount > Invoice.AmountDue) { ErrorMessage = "أدخل مبلغاً أكبر من صفر ولا يتجاوز المتبقي."; return; }
        var result = await _purchasing.RecordPaymentAsync(new RecordPaymentDto { PurchaseInvoiceId = Invoice.Id, Amount = PaymentAmount });
        if (!result.Succeeded) { ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تسجيل الدفعة."; return; }
        await LoadListsAsync(); PaymentAmount = 0; Message = "تم تسجيل الدفعة وتحديث المتبقي للمورد.";
    });
    public void ReportInputError() => ErrorMessage = "راجع الحقول الحمراء وأدخل أرقاماً وتواريخ صحيحة قبل الحفظ.";

    private void GoBack() { if (!IsBusy && IsGoodsStep && !_receiptPosted) Step = PurchaseStep.Supplier; }
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true; ErrorMessage = ""; Message = "";
        try { await action(); }
        catch (Exception ex) { ErrorMessage = "تعذر إكمال الخطوة. تحقق من الاتصال ثم استكمل المستند المحفوظ. " + ex.Message; }
        finally { IsBusy = false; }
    }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(IsSupplierStep), nameof(IsGoodsStep), nameof(IsInvoiceStep), nameof(IsPaymentStep),
            nameof(CanInteract), nameof(CanContinue), nameof(SelectedSupplierId), nameof(CanChangeSupplier), nameof(CanAddSupplier), nameof(SelectedSupplier),
            nameof(ReceiptId), nameof(ReceiptLabel), nameof(NextLabel), nameof(StepTitle), nameof(StepHint), nameof(PaymentStatus) }) OnPropertyChanged(name);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}
