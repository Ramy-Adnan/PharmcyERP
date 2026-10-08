using System.Collections.ObjectModel;
using System.ComponentModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Prescriptions;
using PharmacyERP.Application.Features.Prescriptions.DTOs;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;

namespace PharmacyERP.WPF.ViewModels.Sales;

public class POSViewModel : ViewModelBase
{
    private readonly ISalesService _salesService;
    private readonly IBranchService _branchService;
    private readonly IPrescriptionService _prescriptionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IReceiptPrinter _printer;
    // Serialize scans, customer queries and checkout: the application's EF context is scoped/shared.
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly HashSet<POSLineRow> _observedLines = new();
    private Guid _checkoutRequestId = Guid.NewGuid();
    private SalesInvoiceDetailDto? _lastReceipt;
    private int? _lastInvoiceId;
    private string _searchText = string.Empty;
    private SaleItemLookupDto? _selectedSearchResult;
    private POSLineRow? _selectedCartLine;
    private CustomerOption? _selectedCustomer;
    private ActivePrescriptionSummaryDto? _selectedPrescription;
    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private decimal _amountTendered, _discountAmount, _customerDebt;
    private string _errorMessage = string.Empty, _successMessage = string.Empty;
    private bool _isBusy, _initialized, _checkoutUncertain;
    private int _pendingScans;

    public POSViewModel(ISalesService salesService, IBranchService branchService,
        IPrescriptionService prescriptionService, ICurrentUserService currentUserService,
        IReceiptPrinter printer)
    {
        _salesService = salesService; _branchService = branchService;
        _prescriptionService = prescriptionService; _currentUserService = currentUserService;
        _printer = printer;
        ResolveCheckoutCommand = new AsyncRelayCommand(ResolveCheckoutAsync, () => !IsBusy && CheckoutUncertain);
        SearchCommand = new AsyncRelayCommand(() => SubmitSearchAsync(SearchText), () => !IsBusy);
        AddToCartCommand = new RelayCommand(() => { if (SelectedSearchResult is not null) AddItem(SelectedSearchResult); }, () => !IsBusy && SelectedSearchResult is not null);
        RemoveLineCommand = new RelayCommand(() => { if (SelectedCartLine is not null) CartLines.Remove(SelectedCartLine); }, () => !IsBusy && SelectedCartLine is not null);
        ClearCartCommand = new RelayCommand(() => CartLines.Clear(), () => !IsBusy && CartLines.Count > 0);
        CheckoutCommand = new AsyncRelayCommand(CheckoutAsync, () => !IsBusy && !CheckoutUncertain && _pendingScans == 0 && CartLines.Count > 0);
        PayAllCreditCommand = new RelayCommand(() => AmountTendered = TotalAmount, () => IsCredit && !IsBusy);
        IncrementLineCommand = new RelayCommand(p => AdjustQuantity((POSLineRow)p!, 1), p => !IsBusy && p is POSLineRow line && line.Quantity < line.AvailableQuantity);
        DecrementLineCommand = new RelayCommand(p => AdjustQuantity((POSLineRow)p!, -1), p => !IsBusy && p is POSLineRow line && line.Quantity > 1);
        AddOtherUnitCommand = new RelayCommand(AddOtherUnit, () => !IsBusy && SelectedCartLine?.SaleUnits.Count > 1);
        RemoveItemCommand = new RelayCommand(p => CartLines.Remove((POSLineRow)p!), p => !IsBusy && p is POSLineRow);
        ReprintCommand = new AsyncRelayCommand(ReprintAsync, () => !IsBusy && _lastInvoiceId.HasValue);
        CartLines.CollectionChanged += (_, _) =>
        {
            foreach (var removed in _observedLines.Where(l => !CartLines.Contains(l)).ToList())
            { removed.PropertyChanged -= LineChanged; _observedLines.Remove(removed); }
            foreach (var added in CartLines.Where(l => !_observedLines.Contains(l)))
            { added.PropertyChanged += LineChanged; _observedLines.Add(added); }
            NotifyTotals();
        };
    }

    public event Action? ScanFocusRequested;
    public ObservableCollection<SaleItemLookupDto> SearchResults { get; } = new();
    public ObservableCollection<POSLineRow> CartLines { get; } = new();
    public ObservableCollection<CustomerOption> Customers { get; } = new();
    public ObservableCollection<ActivePrescriptionSummaryDto> FillablePrescriptions { get; } = new();
    public IReadOnlyList<PaymentOption> PaymentMethods { get; } = new[]
    { new PaymentOption(PaymentMethod.Cash, "نقدي"), new PaymentOption(PaymentMethod.Card, "بطاقة"), new PaymentOption(PaymentMethod.Credit, "آجل للعميل المسجل") };
    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }
    public SaleItemLookupDto? SelectedSearchResult { get => _selectedSearchResult; set { SetProperty(ref _selectedSearchResult, value); Requery(); } }
    public POSLineRow? SelectedCartLine { get => _selectedCartLine; set { SetProperty(ref _selectedCartLine, value); Requery(); } }
    public int WarehouseId { get; private set; }
    public CustomerOption? SelectedCustomer
    {
        get => _selectedCustomer;
        set { if (SetProperty(ref _selectedCustomer, value)) { OnPropertyChanged(nameof(HasCreditCustomer)); _ = LoadCustomerAsync(); } }
    }
    public ActivePrescriptionSummaryDto? SelectedPrescription
    {
        get => _selectedPrescription;
        set { SetProperty(ref _selectedPrescription, value); OnPropertyChanged(nameof(HasUnresolvedPrescriptionRequirement)); }
    }
    public bool HasUnresolvedPrescriptionRequirement => CartLines.Any(l => l.RequiresPrescription) && SelectedPrescription is null;
    public PaymentMethod PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (!SetProperty(ref _paymentMethod, value)) return;
            AmountTendered = 0;
            OnPropertyChanged(nameof(IsCash)); OnPropertyChanged(nameof(IsCard)); OnPropertyChanged(nameof(IsCredit));
            OnPropertyChanged(nameof(PaymentHint)); OnPropertyChanged(nameof(CreditRemaining)); OnPropertyChanged(nameof(CustomerBalanceAfterSale)); Requery();
        }
    }
    public bool IsCash { get => PaymentMethod == PaymentMethod.Cash; set { if (value) PaymentMethod = PaymentMethod.Cash; } }
    public bool IsCard { get => PaymentMethod == PaymentMethod.Card; set { if (value) PaymentMethod = PaymentMethod.Card; } }
    public bool IsCredit { get => PaymentMethod == PaymentMethod.Credit; set { if (value) PaymentMethod = PaymentMethod.Credit; } }
    public bool HasCreditCustomer => SelectedCustomer?.Id is not null;
    public string CashierName => _currentUserService.UserName ?? "الكاشير";
    public string PaymentHint => IsCredit ? "استلم جزءاً من المبلغ الآن، وسجّل المتبقي على العميل. اتركه صفراً لآجل كامل."
        : IsCash ? "سيُسجَّل كامل المبلغ نقداً عند إتمام البيع." : "أكد نجاح الدفع على جهاز البطاقة قبل إتمام البيع.";
    public decimal CustomerDebt { get => _customerDebt; private set { SetProperty(ref _customerDebt, value); OnPropertyChanged(nameof(CustomerBalanceAfterSale)); } }
    public decimal AmountTendered { get => _amountTendered; set { SetProperty(ref _amountTendered, value); OnPropertyChanged(nameof(CreditRemaining)); OnPropertyChanged(nameof(CustomerBalanceAfterSale)); } }
    public decimal CreditRemaining => IsCredit ? Math.Max(0, TotalAmount - AmountTendered) : 0;
    public decimal CustomerBalanceAfterSale => CustomerDebt + CreditRemaining;
    public decimal DiscountAmount { get => _discountAmount; set { SetProperty(ref _discountAmount, value); NotifyTotals(); } }
    public decimal SubTotal => CartLines.Sum(l => l.UnitPrice * l.Quantity);
    public decimal TaxAmount => CartLines.Sum(l => l.UnitPrice * l.Quantity * l.TaxRatePercent / 100m);
    public decimal TotalAmount => Math.Round(SubTotal + TaxAmount - CartLines.Sum(l => l.DiscountAmount) - DiscountAmount, 2);
    public int ItemCount => CartLines.Sum(l => l.Quantity);
    public decimal CartDiscount => DiscountAmount + CartLines.Sum(l => l.DiscountAmount);
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public string SuccessMessage { get => _successMessage; set => SetProperty(ref _successMessage, value); }
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); Requery(); } }
    public bool CheckoutUncertain { get => _checkoutUncertain; private set { SetProperty(ref _checkoutUncertain, value); Requery(); } }
    public bool CanScan => _initialized && !IsBusy && !CheckoutUncertain;
    public AsyncRelayCommand ResolveCheckoutCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand AddToCartCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public RelayCommand ClearCartCommand { get; }
    public RelayCommand PayAllCreditCommand { get; }
    public RelayCommand IncrementLineCommand { get; }
    public RelayCommand DecrementLineCommand { get; }
    public RelayCommand AddOtherUnitCommand { get; }
    public RelayCommand RemoveItemCommand { get; }
    public AsyncRelayCommand CheckoutCommand { get; }
    public AsyncRelayCommand ReprintCommand { get; }
    private static void Requery() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    private void NotifyTotals()
    {
        foreach (var name in new[] { nameof(SubTotal), nameof(TaxAmount), nameof(TotalAmount), nameof(CreditRemaining), nameof(CustomerBalanceAfterSale), nameof(CartDiscount), nameof(ItemCount), nameof(HasUnresolvedPrescriptionRequirement) }) OnPropertyChanged(name);
        Requery();
    }
    private void LineChanged(object? sender, PropertyChangedEventArgs e) => NotifyTotals();

    public async Task InitializeAsync()
    {
        if (_initialized) { ScanFocusRequested?.Invoke(); return; }
        await _operations.WaitAsync();
        IsBusy = true;
        try
        {
            var warehouses = await _branchService.GetWarehousesAsync(_currentUserService.CurrentBranchId ?? 0);
            WarehouseId = (warehouses.FirstOrDefault(w => w.IsDefault && w.IsActive) ?? warehouses.FirstOrDefault(w => w.IsActive))?.Id ?? 0;
            var customers = await _salesService.GetCustomersAsync();
            Customers.Clear(); Customers.Add(new CustomerOption { Name = "عميل مباشر — نقدي / بطاقة" });
            foreach (var c in customers.Where(c => c.IsActive)) Customers.Add(new CustomerOption { Id = c.Id, Name = $"{c.Code} — {c.Name}" });
            SelectedCustomer = Customers.First();
            _initialized = true;
            if (WarehouseId == 0) ErrorMessage = "لا يوجد مخزن نشط في الفرع الحالي.";
        }
        catch (Exception ex) { ErrorMessage = $"تعذر تحميل نقطة البيع: {ex.Message}"; }
        finally { IsBusy = false; _operations.Release(); ScanFocusRequested?.Invoke(); }
    }

    private async Task LoadCustomerAsync()
    {
        var id = SelectedCustomer?.Id;
        await _operations.WaitAsync();
        try
        {
            if (SelectedCustomer?.Id != id) return;
            FillablePrescriptions.Clear(); SelectedPrescription = null; CustomerDebt = 0;
            if (id.HasValue)
            {
                var prescriptions = await _prescriptionService.GetFillablePrescriptionsForCustomerAsync(id.Value);
                var account = await _salesService.GetCustomerAccountAsync(id.Value);
                if (SelectedCustomer?.Id != id) return;
                foreach (var p in prescriptions) FillablePrescriptions.Add(p);
                CustomerDebt = account?.OutstandingAmount ?? 0;
            }
        }
        catch (Exception ex) { ErrorMessage = $"تعذر تحميل حساب العميل: {ex.Message}"; }
        finally { _operations.Release(); }
    }

    // Called for every scanner event, rather than ICommand's single-flight guard dropping rapid scans.
    public async Task SubmitSearchAsync(string text, bool barcodeOnly = false)
    {
        text = text.Trim();
        if (text.Length == 0 || !CanScan) return;
        SearchText = string.Empty; // Release the input immediately, before any database round trip.
        _pendingScans++; Requery();
        await _operations.WaitAsync();
        try
        {
            ErrorMessage = string.Empty; SuccessMessage = string.Empty;
            var results = await _salesService.SearchSaleItemsAsync(text, WarehouseId);
            var exact = results.Where(r => (string.Equals(r.Barcode, text, StringComparison.OrdinalIgnoreCase) || string.Equals(r.BaseUnitBarcode, text, StringComparison.OrdinalIgnoreCase))).ToList();
            if (exact.Count == 0 && !barcodeOnly) exact = results.Where(r => string.Equals(r.Code, text, StringComparison.OrdinalIgnoreCase)).ToList();
            SearchResults.Clear();
            if (exact.Count == 1) AddItem(exact[0], text);
            else
            {
                foreach (var item in results) SearchResults.Add(item);
                if (exact.Count > 1) ErrorMessage = "الباركود مسجل لأكثر من صنف؛ اختر الصنف الصحيح وصحح بيانات الأصناف.";
                else if (barcodeOnly || results.Count == 0) ErrorMessage = $"لم يتم العثور على باركود مطابق: {text}";
            }
        }
        catch (Exception ex) { ErrorMessage = $"تعذر قراءة الصنف: {ex.Message}"; }
        finally { _pendingScans--; _operations.Release(); Requery(); ScanFocusRequested?.Invoke(); }
    }

    private void AddItem(SaleItemLookupDto item, string? scannedBarcode = null)
    {
        var scanBase = !string.IsNullOrWhiteSpace(scannedBarcode) && string.Equals(item.BaseUnitBarcode, scannedBarcode, StringComparison.OrdinalIgnoreCase);
        var line = CartLines.LastOrDefault(l => l.ItemId == item.ItemId && (!scanBase || !l.SellAsPackage));
        var factor = scanBase ? 1 : line?.UnitsPerSale ?? (item.AvailableQuantity >= item.UnitsPerPackage ? item.UnitsPerPackage : 1);
        if (ReservedStock(item.ItemId) + factor > item.AvailableQuantity)
        { ErrorMessage = $"المتوفر من {item.DisplayName}: {item.StockDisplay}. اختر الوحدة الصغيرة عند عدم توفر عبوة كاملة."; return; }
        foreach (var existing in CartLines.Where(l => l.ItemId == item.ItemId)) existing.AvailableBaseQuantity = item.AvailableQuantity;
        if (line is not null)
        {
            line.Quantity++;
        }
        else
        {
            var units = new List<SaleUnitOption> { new(false, item.UnitOfMeasureName, 1) };
            if (item.UnitsPerPackage > 1) units.Add(new(true, item.PackageUnitName, item.UnitsPerPackage));
            line = new POSLineRow { ItemId = item.ItemId, Code = item.Code, Name = item.DisplayName,
                UnitOfMeasureName = item.UnitOfMeasureName, TaxRatePercent = item.TaxRatePercent,
                AvailableBaseQuantity = item.AvailableQuantity, RequiresPrescription = item.RequiresPrescription,
                UnitsPerPackage = item.UnitsPerPackage, BaseSalePrice = item.DefaultSalePrice,
                PackageSalePrice = item.UnitsPerPackage > 1 ? item.PackageSalePrice : item.DefaultSalePrice,
                SaleUnits = units, Quantity = 1 };
            line.SelectedSaleUnit = factor == 1 ? units.First() : units.Last();
            CartLines.Add(line);
        }
        SelectedCartLine = line;
        SuccessMessage = $"{item.DisplayName} — {line.Quantity} {line.SelectedUnitName}";
        ScanFocusRequested?.Invoke();
    }

    private long ReservedStock(int itemId) => CartLines.Where(l => l.ItemId == itemId).Sum(l => (long)l.Quantity * l.UnitsPerSale);

    private void AddOtherUnit()
    {
        if (SelectedCartLine is not { } source || source.SaleUnits.Count < 2) return;
        var otherUnit = source.SaleUnits.First(u => u.IsPackage != source.SellAsPackage);
        if (ReservedStock(source.ItemId) + otherUnit.Factor > source.AvailableBaseQuantity)
        { ErrorMessage = "المخزون المتبقي لا يكفي للوحدة الأخرى."; return; }
        var existing = CartLines.FirstOrDefault(l => l.ItemId == source.ItemId && l.SellAsPackage == otherUnit.IsPackage);
        if (existing is not null) { existing.Quantity++; SelectedCartLine = existing; ScanFocusRequested?.Invoke(); return; }
        var row = new POSLineRow
        {
            ItemId = source.ItemId, Code = source.Code, Name = source.Name, UnitOfMeasureName = source.UnitOfMeasureName,
            TaxRatePercent = source.TaxRatePercent, AvailableBaseQuantity = source.AvailableBaseQuantity,
            RequiresPrescription = source.RequiresPrescription, UnitsPerPackage = source.UnitsPerPackage,
            BaseSalePrice = source.BaseSalePrice, PackageSalePrice = source.PackageSalePrice, SaleUnits = source.SaleUnits
        };
        row.SelectedSaleUnit = otherUnit; CartLines.Add(row); SelectedCartLine = row;
        ErrorMessage = string.Empty;
        ScanFocusRequested?.Invoke();
    }

    private void AdjustQuantity(POSLineRow line, int delta)
    {
        if (!CartLines.Contains(line) || line.Quantity + delta < 1 || ReservedStock(line.ItemId) + (long)delta * line.UnitsPerSale > line.AvailableBaseQuantity) return;
        line.Quantity += delta;
        ScanFocusRequested?.Invoke();
    }

    public async Task CheckoutAsync()
    {
        if (IsBusy || CheckoutUncertain || _pendingScans > 0) return;
        ErrorMessage = string.Empty; SuccessMessage = string.Empty;
        if (!CartLines.Any()) { ErrorMessage = "السلة فارغة."; return; }
        if (CartLines.Any(l => l.Quantity <= 0 || l.Quantity > l.AvailableQuantity || l.UnitPrice < 0 || l.DiscountAmount < 0 || l.DiscountAmount > l.UnitPrice * l.Quantity)
            || DiscountAmount < 0 || DiscountAmount + CartLines.Sum(l => l.DiscountAmount) > SubTotal)
        { ErrorMessage = "راجع الكميات والأسعار والخصومات."; return; }
        if (CartLines.GroupBy(l => new { l.ItemId, l.SellAsPackage }).Any(g => g.Count() > 1))
        { ErrorMessage = "يوجد سطران لنفس الصنف والوحدة؛ اجمع الكمية في سطر واحد."; return; }
        if (CartLines.GroupBy(l => l.ItemId).Any(g => g.Sum(l => (long)l.Quantity * l.UnitsPerSale) > g.Min(l => l.AvailableBaseQuantity)))
        { ErrorMessage = "إجمالي العلب والوحدات الصغيرة يتجاوز المخزون المتوفر."; return; }
        if (IsCredit && SelectedCustomer?.Id is null) { ErrorMessage = "اختر عميلاً مسجلاً للبيع الآجل."; return; }
        if (IsCredit && (AmountTendered < 0 || AmountTendered > TotalAmount || Math.Round(AmountTendered, 2) != AmountTendered))
        { ErrorMessage = "المبلغ المستلم الآن يجب أن يكون بين صفر وإجمالي الفاتورة وبمنزلتين عشريتين كحد أقصى."; return; }
        if (HasUnresolvedPrescriptionRequirement) { ErrorMessage = "اختر العميل واربط الوصفة الطبية قبل البيع."; return; }
        if (_currentUserService.UserId is not int userId) { ErrorMessage = "تعذر تحديد المستخدم الحالي."; return; }
        var dto = new SalesCheckoutDto
        {
            RequestId = _checkoutRequestId, BranchId = _currentUserService.CurrentBranchId ?? 0, WarehouseId = WarehouseId,
            CustomerId = SelectedCustomer?.Id, PrescriptionId = SelectedPrescription?.Id, DiscountAmount = DiscountAmount,
            PaymentMethod = PaymentMethod, AmountTendered = IsCredit ? AmountTendered : TotalAmount,
            Lines = CartLines.Select(l => new SaleLineInputDto { ItemId = l.ItemId, SellAsPackage = l.SellAsPackage, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                TaxRatePercent = l.TaxRatePercent, DiscountAmount = l.DiscountAmount }).ToList()
        };
        IsBusy = true;
        await _operations.WaitAsync();
        try
        {
            var result = await _salesService.CheckoutAsync(dto, userId);
            if (!result.Succeeded) { ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر إتمام البيع."; return; }
            await CompleteCommittedSaleAsync(result.Value!);
        }
        catch (Exception ex)
        {
            CheckoutUncertain = true;
            ErrorMessage = $"تعذر التأكد من حفظ العملية: {ex.Message} أعد الاتصال ثم اضغط التحقق من البيع السابق قبل متابعة البيع.";
        }
        finally { IsBusy = false; _operations.Release(); ScanFocusRequested?.Invoke(); }
        if (!CheckoutUncertain) await LoadCustomerAsync();
    }

    private async Task CompleteCommittedSaleAsync(SalesInvoiceDto invoice)
    {
        _lastInvoiceId = invoice.Id; _lastReceipt = null;
        // Clear the committed sale before printing. Printer failure must never allow a duplicate checkout.
        CartLines.Clear(); AmountTendered = 0; DiscountAmount = 0; SearchResults.Clear(); SearchText = string.Empty;
        _checkoutRequestId = Guid.NewGuid(); CheckoutUncertain = false;
        SuccessMessage = $"تم حفظ الفاتورة {invoice.Number}" + (invoice.PaymentMethod == PaymentMethod.Credit
            ? $" — المستلم {invoice.InitialPaymentAmount:N2}، المتبقي على العميل {invoice.DebtAtSale:N2}."
            : " — مدفوعة بالكامل.");
        try
        {
            _lastReceipt = await _salesService.GetSalesInvoiceDetailAsync(invoice.Id)
                ?? throw new InvalidOperationException("تعذر تحميل الوصل.");
            _printer.Print(_lastReceipt);
            SuccessMessage += " أرسلت إلى الطابعة الافتراضية.";
        }
        catch (Exception ex) { ErrorMessage = $"تم البيع وحفظ الفاتورة، لكن تعذرت الطباعة: {ex.Message} استخدم إعادة طباعة آخر وصل."; }
    }

    public async Task ResolveCheckoutAsync()
    {
        IsBusy = true; await _operations.WaitAsync();
        try
        {
            var invoice = await _salesService.FindCheckoutAsync(_checkoutRequestId);
            ErrorMessage = string.Empty;
            if (invoice is not null) await CompleteCommittedSaleAsync(invoice);
            else { CheckoutUncertain = false; ErrorMessage = "لم تحفظ العملية السابقة؛ يمكنك مراجعة السلة وإتمام البيع."; }
        }
        catch (Exception ex) { ErrorMessage = $"ما زال التحقق متعذراً: {ex.Message}"; }
        finally { IsBusy = false; _operations.Release(); ScanFocusRequested?.Invoke(); }
        if (!CheckoutUncertain) await LoadCustomerAsync();
    }

    public async Task ReprintAsync()
    {
        if (!_lastInvoiceId.HasValue) return;
        IsBusy = true; await _operations.WaitAsync();
        try
        {
            _lastReceipt ??= await _salesService.GetSalesInvoiceDetailAsync(_lastInvoiceId.Value);
            if (_lastReceipt is null) throw new InvalidOperationException("الفاتورة غير موجودة.");
            _printer.Print(_lastReceipt); ErrorMessage = string.Empty; SuccessMessage = "أرسل الوصل إلى الطابعة الافتراضية.";
        }
        catch (Exception ex) { ErrorMessage = $"تعذرت الطباعة: {ex.Message}"; }
        finally { IsBusy = false; _operations.Release(); ScanFocusRequested?.Invoke(); }
    }
}

public record PaymentOption(PaymentMethod Value, string Name);
public class CustomerOption
{
    public int? Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
