using System.Collections.ObjectModel;
using System.ComponentModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Inventory;
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
    private readonly IInventoryService? _inventoryService;
    private readonly ProductImageStore? _images;
    // Serialize scans, customer queries and checkout: the application's EF context is scoped/shared.
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly HashSet<POSLineRow> _observedLines = new();
    private readonly Dictionary<int, SaleItemLookupDto> _cartPrices = new();
    private bool _pricingCart;
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
    private string _resultsTitle = "نتائج البحث";
    private int? _catalogCategoryId;
    private bool _hasMoreProducts, _isSearching;
    private bool _isClosing;
    private long _searchVersion;

    public POSViewModel(ISalesService salesService, IBranchService branchService,
        IPrescriptionService prescriptionService, ICurrentUserService currentUserService,
        IReceiptPrinter printer, IInventoryService? inventoryService = null, ProductImageStore? images = null)
    {
        _salesService = salesService; _branchService = branchService;
        _prescriptionService = prescriptionService; _currentUserService = currentUserService;
        _printer = printer;
        _inventoryService = inventoryService;
        _images = images;
        AddProductCommand = new RelayCommand(p => _ = AddProductAsync((POSProductCard)p!), p => CanScan && p is POSProductCard card);
        LoadMoreProductsCommand = new AsyncRelayCommand(LoadMoreProductsAsync, () => CanScan && _pendingScans == 0 && HasMoreProducts && !_isSearching);
        BrowseCategoryCommand = new AsyncRelayCommand(p => BrowseCategoryAsync((POSCategoryOption)p!), p => CanScan && _pendingScans == 0 && p is POSCategoryOption);
        CloseResultsCommand = new RelayCommand(CloseResults, () => !IsBusy);
        ResolveCheckoutCommand = new AsyncRelayCommand(ResolveCheckoutAsync, () => !IsBusy && CheckoutUncertain);
        SearchCommand = new AsyncRelayCommand(() => SubmitSearchAsync(SearchText), () => !IsBusy);
        AddToCartCommand = new RelayCommand(() => { if (SelectedSearchResult is { } item) _ = AddCatalogueItemAsync(item, false); }, () => CanScan && SelectedSearchResult is not null);
        RemoveLineCommand = new RelayCommand(() => { if (SelectedCartLine is not null) CartLines.Remove(SelectedCartLine); }, () => !IsBusy && SelectedCartLine is not null);
        ClearCartCommand = new RelayCommand(() => CartLines.Clear(), () => !IsBusy && CartLines.Count > 0);
        CancelTransactionCommand = new RelayCommand(CancelTransaction, () => CanScan && _pendingScans == 0 && CartLines.Count > 0);
        CheckoutCommand = new AsyncRelayCommand(CheckoutAsync, () => CanScan && _pendingScans == 0 && CartLines.Count > 0);
        PayAllCreditCommand = new RelayCommand(() => AmountTendered = TotalAmount, () => IsCredit && !IsBusy);
        IncrementLineCommand = new RelayCommand(p => AdjustQuantity((POSLineRow)p!, 1), p => !IsBusy && p is POSLineRow line && line.Quantity < line.AvailableQuantity);
        DecrementLineCommand = new RelayCommand(p => AdjustQuantity((POSLineRow)p!, -1), p => !IsBusy && p is POSLineRow line && line.Quantity > 1);
        AddOtherUnitCommand = new RelayCommand(AddOtherUnit, () => !IsBusy && SelectedCartLine?.SaleUnits.Count > 1);
        RemoveItemCommand = new RelayCommand(p => CartLines.Remove((POSLineRow)p!), p => !IsBusy && p is POSLineRow);
        ReprintCommand = new AsyncRelayCommand(ReprintAsync, () => !IsBusy && _lastInvoiceId.HasValue);
        UseBatchPricingCommand = new AsyncRelayCommand(RestoreBatchPricingAsync, () => CanScan && _pendingScans == 0 && SelectedCartLine is not null);
        CartLines.CollectionChanged += (_, _) =>
        {
            foreach (var removed in _observedLines.Where(l => !CartLines.Contains(l)).ToList())
            { removed.PropertyChanged -= LineChanged; _observedLines.Remove(removed); }
            foreach (var added in CartLines.Where(l => !_observedLines.Contains(l)))
            { added.PropertyChanged += LineChanged; _observedLines.Add(added); }
            foreach (var unused in _cartPrices.Keys.Where(id => !CartLines.Any(l => l.ItemId == id)).ToList()) _cartPrices.Remove(unused);
            NotifyTotals();
        };
    }

    public event Action? ScanFocusRequested;
    public ObservableCollection<SaleItemLookupDto> SearchResults { get; } = new();
    public ObservableCollection<POSProductCard> ProductCards { get; } = new();
    public RelayCommand AddProductCommand { get; }
    public AsyncRelayCommand LoadMoreProductsCommand { get; }
    public bool HasMoreProducts { get => _hasMoreProducts; private set => SetProperty(ref _hasMoreProducts, value); }
    public ObservableCollection<POSCategoryOption> Categories { get; } = new();
    public string ResultsTitle { get => _resultsTitle; private set => SetProperty(ref _resultsTitle, value); }
    public AsyncRelayCommand BrowseCategoryCommand { get; }
    public RelayCommand CloseResultsCommand { get; }
    public ObservableCollection<POSLineRow> CartLines { get; } = new();
    public ObservableCollection<CustomerOption> Customers { get; } = new();
    public ObservableCollection<ActivePrescriptionSummaryDto> FillablePrescriptions { get; } = new();
    public IReadOnlyList<PaymentOption> PaymentMethods { get; } = new[]
    { new PaymentOption(PaymentMethod.Cash, "نقدي"), new PaymentOption(PaymentMethod.Credit, "آجل للعميل المسجل") };
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) _searchVersion++; }
    }
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
            if (value is not (PaymentMethod.Cash or PaymentMethod.Credit))
            { ErrorMessage = "نقطة البيع تدعم النقدي والآجل فقط."; return; }
            if (!SetProperty(ref _paymentMethod, value)) return;
            AmountTendered = 0;
            OnPropertyChanged(nameof(IsCash)); OnPropertyChanged(nameof(IsCredit));
            OnPropertyChanged(nameof(PaymentHint)); OnPropertyChanged(nameof(CreditRemaining)); OnPropertyChanged(nameof(CustomerBalanceAfterSale)); Requery();
        }
    }
    public bool IsCash { get => PaymentMethod == PaymentMethod.Cash; set { if (value) PaymentMethod = PaymentMethod.Cash; } }
    public bool IsCredit { get => PaymentMethod == PaymentMethod.Credit; set { if (value) PaymentMethod = PaymentMethod.Credit; } }
    public bool HasCreditCustomer => SelectedCustomer?.Id is not null;
    public string CashierName => _currentUserService.UserName ?? "الكاشير";
    public string PaymentHint => IsCredit ? "استلم جزءاً من المبلغ الآن، وسجّل المتبقي على العميل. اتركه صفراً لآجل كامل."
        : "سيُسجَّل كامل المبلغ نقداً عند إتمام البيع.";
    public decimal CustomerDebt { get => _customerDebt; private set { SetProperty(ref _customerDebt, value); OnPropertyChanged(nameof(CustomerBalanceAfterSale)); } }
    public decimal AmountTendered { get => _amountTendered; set { SetProperty(ref _amountTendered, value); OnPropertyChanged(nameof(CreditRemaining)); OnPropertyChanged(nameof(CustomerBalanceAfterSale)); } }
    public decimal CreditRemaining => IsCredit ? Math.Max(0, TotalAmount - AmountTendered) : 0;
    public decimal CustomerBalanceAfterSale => CustomerDebt + CreditRemaining;
    public decimal DiscountAmount { get => _discountAmount; set { SetProperty(ref _discountAmount, value); NotifyTotals(); } }
    public decimal SubTotal => CartLines.Sum(l => l.SubTotal);
    public decimal TaxAmount => CartLines.Sum(l => l.SubTotal * l.TaxRatePercent / 100m);
    public decimal TotalAmount => Math.Round(SubTotal + TaxAmount - CartLines.Sum(l => l.DiscountAmount) - DiscountAmount, 2);
    public int ItemCount => CartLines.Sum(l => l.Quantity);
    public decimal CartDiscount => DiscountAmount + CartLines.Sum(l => l.DiscountAmount);
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public string SuccessMessage { get => _successMessage; set => SetProperty(ref _successMessage, value); }
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); Requery(); } }
    public bool CheckoutUncertain { get => _checkoutUncertain; private set { SetProperty(ref _checkoutUncertain, value); Requery(); } }
    public bool CanScan => _initialized && !IsBusy && !CheckoutUncertain && !_isClosing;
    public bool CanLeave => !IsBusy && !CheckoutUncertain && _pendingScans == 0;
    public async Task PrepareToCloseAsync()
    {
        _isClosing = true;
        await _operations.WaitAsync();
        _operations.Release();
    }
    public AsyncRelayCommand ResolveCheckoutCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand AddToCartCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public RelayCommand ClearCartCommand { get; }
    public RelayCommand CancelTransactionCommand { get; }
    public RelayCommand PayAllCreditCommand { get; }
    public RelayCommand IncrementLineCommand { get; }
    public RelayCommand DecrementLineCommand { get; }
    public RelayCommand AddOtherUnitCommand { get; }
    public RelayCommand RemoveItemCommand { get; }
    public AsyncRelayCommand CheckoutCommand { get; }
    public AsyncRelayCommand ReprintCommand { get; }
    public AsyncRelayCommand UseBatchPricingCommand { get; }
    private static void Requery() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    private void NotifyTotals()
    {
        if (_pricingCart) return;
        _pricingCart = true;
        try
        {
            var reserved = new Dictionary<int, int>();
            foreach (var line in CartLines)
                if (_cartPrices.TryGetValue(line.ItemId, out var item))
                    line.ApplyPriceQuote(BatchSalePricing.Quote(item.BatchPrices, line.Quantity, line.UnitsPerSale, reserved));
        }
        finally { _pricingCart = false; }
        foreach (var name in new[] { nameof(SubTotal), nameof(TaxAmount), nameof(TotalAmount), nameof(CreditRemaining), nameof(CustomerBalanceAfterSale), nameof(CartDiscount), nameof(ItemCount), nameof(HasUnresolvedPrescriptionRequirement) }) OnPropertyChanged(name);
        Requery();
    }
    private void LineChanged(object? sender, PropertyChangedEventArgs e) => NotifyTotals();

    public async Task InitializeAsync()
    {
        if (_isClosing) return;
        if (_initialized) { ScanFocusRequested?.Invoke(); return; }
        await _operations.WaitAsync();
        IsBusy = true;
        try
        {
            var warehouses = await _branchService.GetWarehousesAsync(_currentUserService.CurrentBranchId ?? 0);
            WarehouseId = (warehouses.FirstOrDefault(w => w.IsDefault && w.IsActive) ?? warehouses.FirstOrDefault(w => w.IsActive))?.Id ?? 0;
            var customers = await _salesService.GetCustomersAsync();
            Customers.Clear(); Customers.Add(new CustomerOption { Name = "عميل نقدي" });
            foreach (var c in customers.Where(c => c.IsActive)) Customers.Add(new CustomerOption { Id = c.Id, Name = $"{c.Code} — {c.Name}" });
            SelectedCustomer = Customers.First();
            Categories.Clear();
            Categories.Add(new POSCategoryOption(null, "جميع الأصناف", "\uE80A", "#EDF3FF", "#3368C6"));
            if (_inventoryService is not null)
            {
                var categories = await _inventoryService.GetCategoriesAsync();
                foreach (var category in categories.Where(c => c.IsActive))
                    Categories.Add(POSCategoryOption.Create(category.Id, category.Name));
            }
            _initialized = true;
            if (WarehouseId > 0)
            {
                var items = await _salesService.BrowseSaleItemsAsync(WarehouseId);
                foreach (var item in items) SearchResults.Add(item);
                DisplayProducts(items);
                Categories[0].IsSelected = true; ResultsTitle = "جميع الأصناف"; HasMoreProducts = items.Count == 60;
            }
            if (WarehouseId == 0) ErrorMessage = "لا يوجد مخزن نشط في الفرع الحالي.";
        }
        catch (Exception ex) { ErrorMessage = $"تعذر تحميل نقطة البيع: {ex.Message}"; }
        finally { IsBusy = false; _operations.Release(); ScanFocusRequested?.Invoke(); }
    }

    public async Task BrowseCategoryAsync(POSCategoryOption category)
    {
        if (!CanScan || _pendingScans > 0) return;
        SearchText = string.Empty;
        await _operations.WaitAsync();
        IsBusy = true;
        try
        {
            ErrorMessage = string.Empty; SuccessMessage = string.Empty;
            var items = await _salesService.BrowseSaleItemsAsync(WarehouseId, category.Id);
            _catalogCategoryId = category.Id; _isSearching = false; HasMoreProducts = items.Count == 60;
            foreach (var option in Categories) option.IsSelected = option == category;
            ResultsTitle = category.Name;
            SearchResults.Clear(); SelectedSearchResult = null;
            foreach (var item in items) SearchResults.Add(item);
            DisplayProducts(items);
            if (items.Count == 0) SuccessMessage = "لا توجد أصناف في هذا التصنيف.";
        }
        catch (Exception ex) { ErrorMessage = $"تعذر تحميل الأصناف: {ex.Message}"; }
        finally { IsBusy = false; _operations.Release(); ScanFocusRequested?.Invoke(); }
    }

    private async Task LoadMoreProductsAsync()
    {
        if (!CanScan || _isSearching || !HasMoreProducts) return;
        await _operations.WaitAsync(); IsBusy = true;
        try
        {
            var items = await _salesService.BrowseSaleItemsAsync(WarehouseId, _catalogCategoryId, skip: ProductCards.Count);
            foreach (var item in items) SearchResults.Add(item);
            foreach (var item in items) ProductCards.Add(new POSProductCard(item, _images?.GetPath(item.ItemId, item.Code)));
            HasMoreProducts = items.Count == 60;
        }
        catch (Exception ex) { ErrorMessage = $"تعذر تحميل الأصناف: {ex.Message}"; }
        finally { IsBusy = false; _operations.Release(); ScanFocusRequested?.Invoke(); }
    }

    public void SetProductImage(POSProductCard card, byte[] data)
    {
        if (_images is null) return;
        var path = _images.Save(card.Item.ItemId, card.Item.Code, data);
        card.PhotoPath = path;
        foreach (var line in CartLines.Where(l => l.ItemId == card.Item.ItemId)) line.PhotoPath = path;
    }

    private void DisplayProducts(IEnumerable<SaleItemLookupDto> items)
    {
        ProductCards.Clear();
        foreach (var item in items) ProductCards.Add(new POSProductCard(item, _images?.GetPath(item.ItemId, item.Code)));
    }

    private async Task RefreshProductsAfterSaleAsync()
    {
        if (_isClosing) return;
        await _operations.WaitAsync(); IsBusy = true;
        try
        {
            var items = await _salesService.BrowseSaleItemsAsync(WarehouseId, _catalogCategoryId);
            DisplayProducts(items); HasMoreProducts = items.Count == 60; _isSearching = false;
            foreach (var category in Categories) category.IsSelected = category.Id == _catalogCategoryId;
            ResultsTitle = Categories.FirstOrDefault(c => c.IsSelected)?.Name ?? "الأصناف";
        }
        catch { ErrorMessage += " تعذر تحديث كروت الأصناف؛ أعد اختيار التصنيف لتحديث المتوفر."; }
        finally { IsBusy = false; _operations.Release(); }
    }

    private void CloseResults()
    {
        SearchResults.Clear(); SelectedSearchResult = null;
        HasMoreProducts = false;
        foreach (var option in Categories) option.IsSelected = false;
        ScanFocusRequested?.Invoke();
    }

    private void CancelTransaction()
    {
        CartLines.Clear(); DiscountAmount = 0; AmountTendered = 0;
        SearchText = string.Empty; SelectedPrescription = null;
        _checkoutRequestId = Guid.NewGuid();
        CloseResults(); ErrorMessage = string.Empty; SuccessMessage = "تم إلغاء المعاملة الحالية.";
    }

    public async Task ShowCustomerProfileAsync(Func<int, Task> showProfile)
    {
        var id = SelectedCustomer?.Id;
        if (!id.HasValue || !CanScan || _pendingScans > 0) return;
        await _operations.WaitAsync();
        IsBusy = true;
        try { await showProfile(id.Value); }
        catch (Exception ex) { ErrorMessage = $"تعذر فتح ملف العميل: {ex.Message}"; }
        finally { IsBusy = false; _operations.Release(); }
        await LoadCustomerAsync(); // Reflect payments recorded in the customer's account.
        ScanFocusRequested?.Invoke();
    }

    private async Task LoadCustomerAsync()
    {
        if (_isClosing) return;
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

    // Live typing updates the catalogue only; explicit scanner submissions add to the basket.
    public async Task RefreshSearchResultsAsync()
    {
        if (!CanScan) return;
        var text = SearchText.Trim();
        var version = _searchVersion;
        await _operations.WaitAsync();
        try
        {
            if (!CanScan || version != _searchVersion) return;
            var items = text.Length == 0
                ? await _salesService.BrowseSaleItemsAsync(WarehouseId, _catalogCategoryId)
                : await _salesService.SearchSaleItemsAsync(text, WarehouseId);
            // A slower result must never replace cards for newer text or after leaving POS.
            if (!CanScan || version != _searchVersion) return;
            SearchResults.Clear(); SelectedSearchResult = null;
            foreach (var item in items) SearchResults.Add(item);
            DisplayProducts(items);
            _isSearching = text.Length > 0;
            HasMoreProducts = !_isSearching && items.Count == 60;
            foreach (var category in Categories) category.IsSelected = !_isSearching && category.Id == _catalogCategoryId;
            ResultsTitle = !_isSearching ? Categories.FirstOrDefault(c => c.IsSelected)?.Name ?? "الأصناف"
                : items.Count == 0 ? "لا توجد نتائج للبحث"
                : items.Count == 30 ? "أول 30 نتيجة · أدخل اسمًا أدق للمزيد" : "نتائج البحث";
            Requery();
        }
        catch (Exception ex)
        {
            if (!_isClosing && version == _searchVersion) ErrorMessage = $"تعذر البحث: {ex.Message}";
        }
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
            var exact = results.Where(r => (string.Equals(r.Barcode, text, StringComparison.OrdinalIgnoreCase) || string.Equals(r.BaseUnitBarcode, text, StringComparison.OrdinalIgnoreCase) || r.SaleUnits.Any(u => string.Equals(u.Barcode, text, StringComparison.OrdinalIgnoreCase)))).ToList();
            if (exact.Count == 0 && !barcodeOnly) exact = results.Where(r => string.Equals(r.Code, text, StringComparison.OrdinalIgnoreCase)).ToList();
            SearchResults.Clear();
            if (exact.Count == 1) AddItem(exact[0], text);
            else
            {
                foreach (var category in Categories) category.IsSelected = false;
                ResultsTitle = results.Count == 30 ? "أول 30 نتيجة · أدخل اسمًا أدق للمزيد" : "نتائج البحث";
                _isSearching = true; HasMoreProducts = false;
                foreach (var item in results) SearchResults.Add(item);
                DisplayProducts(results);
                if (exact.Count > 1) ErrorMessage = "الباركود مسجل لأكثر من صنف؛ اختر الصنف الصحيح وصحح بيانات الأصناف.";
                else if (barcodeOnly || results.Count == 0) ErrorMessage = $"لم يتم العثور على باركود مطابق: {text}";
            }
        }
        catch (Exception ex) { ErrorMessage = $"تعذر قراءة الصنف: {ex.Message}"; }
        finally { _pendingScans--; _operations.Release(); Requery(); ScanFocusRequested?.Invoke(); }
    }

    public Task AddProductAsync(POSProductCard card) => AddCatalogueItemAsync(card.Item, true);

    private async Task AddCatalogueItemAsync(SaleItemLookupDto cachedItem, bool useDisplayedUnit)
    {
        if (!CanScan) return;
        _pendingScans++; Requery();
        await _operations.WaitAsync();
        try
        {
            if (!CanScan) return;
            ErrorMessage = string.Empty; SuccessMessage = string.Empty;
            var results = await _salesService.SearchSaleItemsAsync(cachedItem.Code, WarehouseId);
            if (!CanScan) return;
            var item = results.SingleOrDefault(i => i.ItemId == cachedItem.ItemId);
            if (item is null) { ErrorMessage = "الصنف لم يعد متاحاً للبيع؛ حدّث قائمة الأصناف."; return; }
            // Catalogue cards are snapshots. Stock and prices may have changed since opening POS.
            var cardIndex = ProductCards.ToList().FindIndex(c => c.Item.ItemId == item.ItemId);
            if (cardIndex >= 0) ProductCards[cardIndex] = new POSProductCard(item, _images?.GetPath(item.ItemId, item.Code));
            AddItem(item, preferredPackage: useDisplayedUnit ? item.UnitsPerPackage > 1 && item.AvailableQuantity >= item.UnitsPerPackage : null);
        }
        catch (Exception ex) { ErrorMessage = $"تعذر تحديث سعر ومخزون الصنف: {ex.Message}"; }
        finally { _pendingScans--; _operations.Release(); Requery(); ScanFocusRequested?.Invoke(); }
    }

    private void AddItem(SaleItemLookupDto item, string? scannedBarcode = null, bool? preferredPackage = null)
    {
        _cartPrices[item.ItemId] = item;
        foreach (var existing in CartLines.Where(l => l.ItemId == item.ItemId)) existing.AvailableBaseQuantity = item.AvailableQuantity;
        NotifyTotals();
        var scanBase = !string.IsNullOrWhiteSpace(scannedBarcode) && string.Equals(item.BaseUnitBarcode, scannedBarcode, StringComparison.OrdinalIgnoreCase);
        var scannedUnit = item.SaleUnits.FirstOrDefault(u => !string.IsNullOrWhiteSpace(scannedBarcode) && string.Equals(u.Barcode, scannedBarcode, StringComparison.OrdinalIgnoreCase));
        var line = preferredPackage.HasValue ? CartLines.FirstOrDefault(l => l.ItemId == item.ItemId && l.SellAsPackage == preferredPackage.Value && l.ItemSaleUnitId is null)
            : scannedUnit is not null ? CartLines.FirstOrDefault(l => l.ItemId == item.ItemId && l.ItemSaleUnitId == scannedUnit.Id) : scanBase
            ? CartLines.FirstOrDefault(l => l.ItemId == item.ItemId && !l.SellAsPackage && l.ItemSaleUnitId is null)
            : SelectedCartLine is { } selected && CartLines.Contains(selected) && selected.ItemId == item.ItemId
                ? selected : CartLines.LastOrDefault(l => l.ItemId == item.ItemId);
        var factor = preferredPackage.HasValue ? preferredPackage.Value ? item.UnitsPerPackage : 1
            : scannedUnit?.BaseUnitCount ?? (scanBase ? 1 : line?.UnitsPerSale ?? (item.AvailableQuantity >= item.UnitsPerPackage ? item.UnitsPerPackage : 1));
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
            units.AddRange(item.SaleUnits.Select(u => new SaleUnitOption(false, u.Name, u.BaseUnitCount, u.Id, u.Barcode, u.SalePrice)));
            line = new POSLineRow { ItemId = item.ItemId, Code = item.Code, Name = item.DisplayName,
                PhotoPath = _images?.GetPath(item.ItemId, item.Code),
                UnitOfMeasureName = item.UnitOfMeasureName, TaxRatePercent = item.TaxRatePercent,
                AvailableBaseQuantity = item.AvailableQuantity, RequiresPrescription = item.RequiresPrescription,
                UnitsPerPackage = item.UnitsPerPackage, BaseSalePrice = item.DefaultSalePrice,
                PackageSalePrice = item.UnitsPerPackage > 1 ? item.PackageSalePrice : item.DefaultSalePrice,
                SaleUnits = units, Quantity = 1 };
            line.SelectedSaleUnit = scannedUnit is not null ? units.First(u => u.ItemSaleUnitId == scannedUnit.Id) : factor == 1 ? units.First() : units.First(u => u.IsPackage);
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
        var selectedIndex = source.SaleUnits.ToList().IndexOf(source.SelectedSaleUnit!);
        var otherUnit = source.SaleUnits[(selectedIndex + 1) % source.SaleUnits.Count];
        if (ReservedStock(source.ItemId) + otherUnit.Factor > source.AvailableBaseQuantity)
        { ErrorMessage = "المخزون المتبقي لا يكفي للوحدة الأخرى."; return; }
        var existing = CartLines.FirstOrDefault(l => l.ItemId == source.ItemId && l.SellAsPackage == otherUnit.IsPackage && l.ItemSaleUnitId == otherUnit.ItemSaleUnitId);
        if (existing is not null) { existing.Quantity++; SelectedCartLine = existing; ScanFocusRequested?.Invoke(); return; }
        var row = new POSLineRow
        {
            ItemId = source.ItemId, Code = source.Code, Name = source.Name, UnitOfMeasureName = source.UnitOfMeasureName,
            PhotoPath = source.PhotoPath,
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
        if (_isClosing || IsBusy || CheckoutUncertain || _pendingScans > 0) return;
        ErrorMessage = string.Empty; SuccessMessage = string.Empty;
        if (!CartLines.Any()) { ErrorMessage = "السلة فارغة."; return; }
        if (CartLines.FirstOrDefault(l => l.HasZeroSalePrice) is { } unpriced)
        { ErrorMessage = $"سعر البيع للصنف {unpriced.Name} صفر. حدّد سعر بيع صحيحاً قبل إتمام البيع؛ راجع سعر الدفعة ووحدة البيع."; return; }
        if (CartLines.Any(l => l.Quantity <= 0 || l.Quantity > l.AvailableQuantity || l.UnitPrice < 0 || l.DiscountAmount < 0 || l.DiscountAmount > l.SubTotal)
            || DiscountAmount < 0 || DiscountAmount + CartLines.Sum(l => l.DiscountAmount) > SubTotal)
        { ErrorMessage = "راجع الكميات والأسعار والخصومات."; return; }
        if (CartLines.GroupBy(l => new { l.ItemId, l.SellAsPackage, l.ItemSaleUnitId }).Any(g => g.Count() > 1))
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
            Lines = CartLines.Select(l => new SaleLineInputDto { ItemId = l.ItemId, SellAsPackage = l.SellAsPackage, ItemSaleUnitId = l.ItemSaleUnitId, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                TaxRatePercent = l.TaxRatePercent, DiscountAmount = l.DiscountAmount,
                UseBatchPricing = l.UsesBatchPricing, ExpectedPrices = l.PriceSegments.ToList() }).ToList()
        };
        IsBusy = true;
        await _operations.WaitAsync();
        try
        {
            var result = await _salesService.CheckoutAsync(dto, userId);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر إتمام البيع.";
                if (result.Errors.Contains(BatchSalePricing.PricesChangedMessage))
                {
                    try { await RefreshCartPricesCoreAsync(); }
                    catch (Exception ex) { ErrorMessage += $" تعذّر تحديث الأسعار: {ex.Message}"; }
                }
                return;
            }
            await CompleteCommittedSaleAsync(result.Value!);
        }
        catch (Exception ex)
        {
            CheckoutUncertain = true;
            ErrorMessage = $"تعذر التأكد من حفظ العملية: {ex.Message} أعد الاتصال ثم اضغط التحقق من البيع السابق قبل متابعة البيع.";
        }
        finally { IsBusy = false; _operations.Release(); ScanFocusRequested?.Invoke(); }
        if (!CheckoutUncertain)
        {
            await LoadCustomerAsync();
            await RefreshProductsAfterSaleAsync();
        }
    }

    private async Task RefreshCartPricesCoreAsync()
    {
        foreach (var itemGroup in CartLines.GroupBy(l => l.ItemId).ToList())
        {
            var items = await _salesService.SearchSaleItemsAsync(itemGroup.First().Code, WarehouseId);
            if (items.SingleOrDefault(i => i.ItemId == itemGroup.Key) is not { } item) continue;
            _cartPrices[item.ItemId] = item;
            foreach (var line in itemGroup) line.AvailableBaseQuantity = item.AvailableQuantity;
        }
        NotifyTotals();
    }

    private async Task RestoreBatchPricingAsync()
    {
        var selected = SelectedCartLine;
        if (selected is null || !CanScan || _pendingScans > 0) return;
        IsBusy = true;
        await _operations.WaitAsync();
        try
        {
            await RefreshCartPricesCoreAsync(); selected.UseAutomaticPricing();
            ErrorMessage = string.Empty; SuccessMessage = "تم تحديث الأسعار حسب الدفعات.";
        }
        catch (Exception ex) { ErrorMessage = $"تعذر تحديث أسعار الدفعات: {ex.Message}"; }
        finally { IsBusy = false; _operations.Release(); ScanFocusRequested?.Invoke(); }
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
public sealed class POSProductCard : ViewModelBase
{
    private string? _photoPath;
    public POSProductCard(SaleItemLookupDto item, string? photoPath) { Item = item; _photoPath = photoPath; }
    public SaleItemLookupDto Item { get; }
    public string? PhotoPath { get => _photoPath; set { _photoPath = value; OnPropertyChanged(); } }
    public bool UsePackage => Item.UnitsPerPackage > 1 && Item.AvailableQuantity >= Item.UnitsPerPackage;
    public decimal Price => UsePackage ? Item.PackageSalePrice : Item.DefaultSalePrice;
    public string UnitName => UsePackage ? Item.PackageUnitName : Item.UnitOfMeasureName;
    public string StockText => $"{Item.AvailableQuantity / (UsePackage ? Item.UnitsPerPackage : 1)} {UnitName}";
    public string StockStatus => Item.AvailableQuantity > 0 ? "متوفر" : "غير متوفر";
}
public sealed class POSCategoryOption : ViewModelBase
{
    private bool _isSelected;
    public POSCategoryOption(int? id, string name, string icon, string background, string accent)
    { Id = id; Name = name; Icon = icon; Background = background; Accent = accent; }
    public int? Id { get; }
    public string Name { get; }
    public string Icon { get; }
    public string Background { get; }
    public string Accent { get; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public static POSCategoryOption Create(int id, string name)
    {
        // These keywords choose presentation only; filtering always uses the actual category ID.
        if (name.Contains("مكمل") || name.Contains("فيتامين")) return new(id, name, "\uE8BE", "#F3EEFF", "#8B5FC4");
        if (name.Contains("بشر") || name.Contains("جلد")) return new(id, name, "\uE9CE", "#FFF0E6", "#C77B42");
        if (name.Contains("تجميل")) return new(id, name, "\uE790", "#FDECF3", "#C76691");
        if (name.Contains("شعر")) return new(id, name, "\uE7A6", "#E9F4FF", "#5285BA");
        if (name.Contains("أطفال") || name.Contains("اطفال")) return new(id, name, "\uE8D7", "#FFF7DD", "#B99732");
        return new(id, name, "\uE95E", "#E7F7F1", "#33987D");
    }
}
public class CustomerOption
{
    public int? Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
