using System.Collections.ObjectModel;
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

/// <summary>
/// The cash-register (POS) screen: search/scan an item, build a cart, pick a
/// customer and payment method, and checkout. Warehouse defaults to the
/// cashier's session branch's default warehouse so a typical cashier never
/// has to think about it, while still being changeable for multi-warehouse branches.
/// </summary>
public class POSViewModel : ViewModelBase
{
    private readonly ISalesService _salesService;
    private readonly IBranchService _branchService;
    private readonly IPrescriptionService _prescriptionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDialogService _dialogService;

    private string _searchText = string.Empty;
    private SaleItemLookupDto? _selectedSearchResult;
    private POSLineRow? _selectedCartLine;
    private int _warehouseId;
    private CustomerOption? _selectedCustomer;
    private ActivePrescriptionSummaryDto? _selectedPrescription;
    private PaymentMethod _paymentMethod = PaymentMethod.Cash;
    private decimal _amountTendered;
    private decimal _discountAmount;
    private string _errorMessage = string.Empty;
    private string _successMessage = string.Empty;
    private bool _isBusy;

    public POSViewModel(
        ISalesService salesService, IBranchService branchService, IPrescriptionService prescriptionService,
        ICurrentUserService currentUserService, IDialogService dialogService)
    {
        _salesService = salesService;
        _branchService = branchService;
        _prescriptionService = prescriptionService;
        _currentUserService = currentUserService;
        _dialogService = dialogService;

        SearchResults = new ObservableCollection<SaleItemLookupDto>();
        CartLines = new ObservableCollection<POSLineRow>();
        Customers = new ObservableCollection<CustomerOption>();
        FillablePrescriptions = new ObservableCollection<ActivePrescriptionSummaryDto>();
        PaymentMethods = new ObservableCollection<PaymentMethod>(Enum.GetValues<PaymentMethod>());

        SearchCommand = new AsyncRelayCommand(SearchAsync);
        AddToCartCommand = new RelayCommand(AddSelectedToCart, () => SelectedSearchResult is not null);
        RemoveLineCommand = new RelayCommand(() => { if (SelectedCartLine is not null) CartLines.Remove(SelectedCartLine); }, () => SelectedCartLine is not null);
        ClearCartCommand = new RelayCommand(() => CartLines.Clear(), () => CartLines.Count > 0);
        CheckoutCommand = new AsyncRelayCommand(CheckoutAsync, () => !IsBusy && CartLines.Count > 0);

        CartLines.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SubTotal));
            OnPropertyChanged(nameof(TaxAmount));
            OnPropertyChanged(nameof(TotalAmount));
            OnPropertyChanged(nameof(ChangeDue));
            OnPropertyChanged(nameof(HasUnresolvedPrescriptionRequirement));
        };
    }

    public ObservableCollection<SaleItemLookupDto> SearchResults { get; }
    public ObservableCollection<POSLineRow> CartLines { get; }
    public ObservableCollection<CustomerOption> Customers { get; }
    public ObservableCollection<PaymentMethod> PaymentMethods { get; }

    public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }

    public SaleItemLookupDto? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set { if (SetProperty(ref _selectedSearchResult, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public POSLineRow? SelectedCartLine
    {
        get => _selectedCartLine;
        set { if (SetProperty(ref _selectedCartLine, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public int WarehouseId { get => _warehouseId; set => SetProperty(ref _warehouseId, value); }

    public CustomerOption? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
                _ = LoadFillablePrescriptionsAsync();
        }
    }

    public ObservableCollection<ActivePrescriptionSummaryDto> FillablePrescriptions { get; }

    public ActivePrescriptionSummaryDto? SelectedPrescription
    {
        get => _selectedPrescription;
        set
        {
            if (SetProperty(ref _selectedPrescription, value))
                OnPropertyChanged(nameof(HasUnresolvedPrescriptionRequirement));
        }
    }

    /// <summary>True when the cart contains a prescription-only item but no prescription has been linked yet — surfaced in the View to warn the cashier before checkout is attempted.</summary>
    public bool HasUnresolvedPrescriptionRequirement =>
        CartLines.Any(l => l.RequiresPrescription) && SelectedPrescription is null;

    public PaymentMethod PaymentMethod
    {
        get => _paymentMethod;
        set { if (SetProperty(ref _paymentMethod, value)) OnPropertyChanged(nameof(ChangeDue)); }
    }

    public decimal AmountTendered
    {
        get => _amountTendered;
        set { if (SetProperty(ref _amountTendered, value)) OnPropertyChanged(nameof(ChangeDue)); }
    }

    public decimal DiscountAmount
    {
        get => _discountAmount;
        set { if (SetProperty(ref _discountAmount, value)) { OnPropertyChanged(nameof(TotalAmount)); OnPropertyChanged(nameof(ChangeDue)); } }
    }

    public decimal SubTotal => CartLines.Sum(l => l.UnitPrice * l.Quantity);
    public decimal TaxAmount => CartLines.Sum(l => l.UnitPrice * l.Quantity * l.TaxRatePercent / 100m);
    public decimal TotalAmount => Math.Max(0, Math.Round(SubTotal + TaxAmount - CartLines.Sum(l => l.DiscountAmount) - DiscountAmount, 2));
    public decimal ChangeDue => PaymentMethod == PaymentMethod.Cash ? Math.Max(0, AmountTendered - TotalAmount) : 0;

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public string SuccessMessage { get => _successMessage; set => SetProperty(ref _successMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand AddToCartCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public RelayCommand ClearCartCommand { get; }
    public AsyncRelayCommand CheckoutCommand { get; }

    public async Task InitializeAsync()
    {
        var branchId = _currentUserService.CurrentBranchId ?? 0;
        if (branchId > 0)
        {
            var warehouses = await _branchService.GetWarehousesAsync(branchId);
            var defaultWarehouse = warehouses.FirstOrDefault(w => w.IsDefault && w.IsActive) ?? warehouses.FirstOrDefault(w => w.IsActive);
            if (defaultWarehouse is not null) WarehouseId = defaultWarehouse.Id;
        }

        var customers = await _salesService.GetCustomersAsync();
        Customers.Clear();
        Customers.Add(new CustomerOption { Id = null, Name = "بدون عميل (بيع نقدي مباشر)" });
        foreach (var c in customers.Where(c => c.IsActive))
            Customers.Add(new CustomerOption { Id = c.Id, Name = c.Name });
        SelectedCustomer = Customers.First();
    }

    private async Task LoadFillablePrescriptionsAsync()
    {
        FillablePrescriptions.Clear();
        SelectedPrescription = null;

        if (SelectedCustomer?.Id is null) return;

        var prescriptions = await _prescriptionService.GetFillablePrescriptionsForCustomerAsync(SelectedCustomer.Id.Value);
        foreach (var p in prescriptions) FillablePrescriptions.Add(p);
    }

    private async Task SearchAsync()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SearchText) || WarehouseId <= 0) return;

        var results = await _salesService.SearchSaleItemsAsync(SearchText.Trim(), WarehouseId);
        foreach (var r in results) SearchResults.Add(r);
    }

    private void AddSelectedToCart()
    {
        if (SelectedSearchResult is null) return;
        var item = SelectedSearchResult;

        if (item.AvailableQuantity <= 0)
        {
            _dialogService.ShowError("لا توجد كمية متوفرة من هذا الصنف في المخزن الحالي.");
            return;
        }

        var existingLine = CartLines.FirstOrDefault(l => l.ItemId == item.ItemId);
        if (existingLine is not null)
        {
            if (existingLine.Quantity + 1 > item.AvailableQuantity)
            {
                _dialogService.ShowError($"الكمية المتوفرة من '{item.Name}' هي {item.AvailableQuantity} فقط.");
                return;
            }
            existingLine.Quantity++;
        }
        else
        {
            CartLines.Add(new POSLineRow
            {
                ItemId = item.ItemId,
                Code = item.Code,
                Name = item.Name,
                UnitOfMeasureName = item.UnitOfMeasureName,
                TaxRatePercent = item.TaxRatePercent,
                AvailableQuantity = item.AvailableQuantity,
                RequiresPrescription = item.RequiresPrescription,
                UnitPrice = item.DefaultSalePrice,
                Quantity = 1
            });
        }

        OnPropertyChanged(nameof(SubTotal));
        OnPropertyChanged(nameof(TaxAmount));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(ChangeDue));
        OnPropertyChanged(nameof(HasUnresolvedPrescriptionRequirement));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task CheckoutAsync()
    {
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        if (!CartLines.Any()) { ErrorMessage = "السلة فارغة."; return; }
        if (CartLines.Any(l => l.Quantity > l.AvailableQuantity))
        {
            ErrorMessage = "إحدى الكميات في السلة تتجاوز المتوفر في المخزون. الرجاء تحديث السلة.";
            return;
        }
        if (HasUnresolvedPrescriptionRequirement)
        {
            ErrorMessage = "السلة تحتوي على صنف يتطلب وصفة طبية. الرجاء اختيار العميل ثم ربط الوصفة الطبية المناسبة قبل إتمام البيع.";
            return;
        }

        var cashierUserId = _currentUserService.UserId;
        if (cashierUserId is null) { ErrorMessage = "تعذر تحديد المستخدم الحالي."; return; }

        IsBusy = true;
        try
        {
            var dto = new SalesCheckoutDto
            {
                BranchId = _currentUserService.CurrentBranchId ?? 0,
                WarehouseId = WarehouseId,
                CustomerId = SelectedCustomer?.Id,
                PrescriptionId = SelectedPrescription?.Id,
                DiscountAmount = DiscountAmount,
                PaymentMethod = PaymentMethod,
                AmountTendered = PaymentMethod == PaymentMethod.Cash ? AmountTendered : TotalAmount,
                Lines = CartLines.Select(l => new SaleLineInputDto
                {
                    ItemId = l.ItemId,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    TaxRatePercent = l.TaxRatePercent,
                    DiscountAmount = l.DiscountAmount
                }).ToList()
            };

            var result = await _salesService.CheckoutAsync(dto, cashierUserId.Value);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر إتمام عملية البيع.";
                return;
            }

            SuccessMessage = PaymentMethod == PaymentMethod.Cash
                ? $"تمت العملية بنجاح - فاتورة رقم {result.Value!.Number}. الباقي للعميل: {result.Value.ChangeGiven:N2}"
                : $"تمت العملية بنجاح - فاتورة رقم {result.Value!.Number}.";

            CartLines.Clear();
            AmountTendered = 0;
            DiscountAmount = 0;
            SearchResults.Clear();
            SearchText = string.Empty;
            await LoadFillablePrescriptionsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Lightweight customer option for the POS customer picker, including the "walk-in" null option.</summary>
public class CustomerOption
{
    public int? Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
