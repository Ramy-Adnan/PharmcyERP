using System.Collections.ObjectModel;
using System.ComponentModel;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

/// <summary>
/// ViewModel for creating a Purchase Invoice, either from scratch or
/// pre-filled from an already-Posted Goods Receipt so the accountant doesn't
/// retype quantities/costs (PrefillInvoiceFromGoodsReceiptAsync).
/// </summary>
public class PurchaseInvoiceEditViewModel : PurchasePricingViewModel
{
    private readonly IPurchasingService _purchasingService;
    private readonly IInventoryService _inventoryService;
    private readonly IBranchService _branchService;

    private int _supplierId;
    private int? _goodsReceiptNoteId;
    private int _branchId;
    private DateTime _invoiceDate = DateTime.Today;
    private DateTime? _dueDate;
    private decimal _discountAmount;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private PILineRow? _selectedLine;

    public PurchaseInvoiceEditViewModel(IPurchasingService purchasingService, IInventoryService inventoryService, IBranchService branchService)
    {
        _purchasingService = purchasingService;
        _inventoryService = inventoryService;
        _branchService = branchService;

        Suppliers = new ObservableCollection<SupplierDto>();
        PostedGoodsReceipts = new ObservableCollection<GoodsReceiptNoteDto>();
        Branches = new ObservableCollection<BranchDto>();
        AvailableItems = new ObservableCollection<ItemDto>();
        Lines = new ObservableCollection<PILineRow>();
        Lines.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (PILineRow line in e.OldItems) line.PropertyChanged -= LineChanged;
            if (e.NewItems is not null)
                foreach (PILineRow line in e.NewItems) line.PropertyChanged += LineChanged;
            OnPropertyChanged(nameof(GrandTotal));
        };

        LoadFromGoodsReceiptCommand = new AsyncRelayCommand(LoadFromGoodsReceiptAsync, () => GoodsReceiptNoteId.HasValue);
        AddLineCommand = new RelayCommand(() => Lines.Add(new PILineRow()));
        RemoveLineCommand = new RelayCommand(() => { if (SelectedLine is not null) Lines.Remove(SelectedLine); }, () => SelectedLine is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public ObservableCollection<SupplierDto> Suppliers { get; }
    public ObservableCollection<GoodsReceiptNoteDto> PostedGoodsReceipts { get; }
    public ObservableCollection<BranchDto> Branches { get; }
    public ObservableCollection<ItemDto> AvailableItems { get; }
    public ObservableCollection<PILineRow> Lines { get; }

    public int SupplierId
    {
        get => _supplierId;
        set
        {
            if (SetProperty(ref _supplierId, value))
                _ = LoadPostedGoodsReceiptsForSupplierAsync();
        }
    }

    public int? GoodsReceiptNoteId
    {
        get => _goodsReceiptNoteId;
        set
        {
            if (SetProperty(ref _goodsReceiptNoteId, value))
            {
                var receipt = PostedGoodsReceipts.FirstOrDefault(n => n.Id == value);
                if (receipt is not null) PurchaseType = receipt.PurchaseType;
                OnPropertyChanged(nameof(CanChoosePurchaseType));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanChoosePurchaseType => !GoodsReceiptNoteId.HasValue;
    public int BranchId { get => _branchId; set => SetProperty(ref _branchId, value); }
    public DateTime InvoiceDate { get => _invoiceDate; set => SetProperty(ref _invoiceDate, value); }
    public DateTime? DueDate { get => _dueDate; set => SetProperty(ref _dueDate, value); }

    public decimal DiscountAmount
    {
        get => _discountAmount;
        set { if (SetProperty(ref _discountAmount, value)) OnPropertyChanged(nameof(GrandTotal)); }
    }

    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public decimal GrandTotal => Math.Max(0, Lines.Sum(l => l.LineTotal) - DiscountAmount);

    private void LineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PILineRow.LineTotal)) OnPropertyChanged(nameof(GrandTotal));
    }

    public PILineRow? SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (SetProperty(ref _selectedLine, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand LoadFromGoodsReceiptCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public PurchaseInvoiceDto? SavedInvoice { get; private set; }
    public event Action? RequestClose;

    public async Task LoadForCreateAsync()
    {
        var suppliers = await _purchasingService.GetSuppliersAsync();
        Suppliers.Clear();
        foreach (var s in suppliers.Where(s => s.IsActive)) Suppliers.Add(s);

        var branches = await _branchService.GetAllAsync(includeInactive: false);
        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);

        var items = await _inventoryService.GetItemsAsync();
        AvailableItems.Clear();
        foreach (var i in items.Where(i => i.IsActive)) AvailableItems.Add(i);

        if (Branches.Count > 0) BranchId = Branches.First().Id;
    }

    /// <summary>Pre-selects a specific Goods Receipt to invoice, called when the user opens this dialog from the Goods Receipt screen.</summary>
    public async Task PreselectGoodsReceiptAsync(int goodsReceiptNoteId)
    {
        var prefill = await _purchasingService.PrefillInvoiceFromGoodsReceiptAsync(goodsReceiptNoteId);
        if (prefill is null) return;

        SetProperty(ref _supplierId, prefill.SupplierId, nameof(SupplierId));
        await LoadPostedGoodsReceiptsForSupplierAsync();
        GoodsReceiptNoteId = prefill.GoodsReceiptNoteId;
        BranchId = prefill.BranchId;
        InvoiceDate = prefill.InvoiceDate;
        PurchaseType = prefill.PurchaseType;

        Lines.Clear();
        foreach (var line in prefill.Lines)
        {
            var item = AvailableItems.FirstOrDefault(i => i.Id == line.ItemId);
            Lines.Add(new PILineRow
            {
                ItemId = line.ItemId,
                ItemCode = item?.Code ?? string.Empty,
                ItemName = item?.DisplayName ?? string.Empty,
                PurchaseUnitDescription = item?.PackagingDescription ?? string.Empty,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                TaxRatePercent = line.TaxRatePercent,
                DiscountAmount = line.DiscountAmount
            });
        }
    }

    private async Task LoadPostedGoodsReceiptsForSupplierAsync()
    {
        PostedGoodsReceipts.Clear();
        if (SupplierId <= 0) return;

        var allNotes = await _purchasingService.GetGoodsReceiptNotesAsync();
        foreach (var n in allNotes.Where(n => n.SupplierName == Suppliers.FirstOrDefault(s => s.Id == SupplierId)?.Name
                     && n.Status == GoodsReceiptStatus.Posted))
        {
            PostedGoodsReceipts.Add(n);
        }
    }

    private async Task LoadFromGoodsReceiptAsync()
    {
        if (!GoodsReceiptNoteId.HasValue) return;
        await PreselectGoodsReceiptAsync(GoodsReceiptNoteId.Value);
    }

    public void ApplyItemSelection(PILineRow line, int itemId)
    {
        var item = AvailableItems.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;

        line.ItemId = item.Id;
        line.ItemCode = item.Code;
        line.ItemName = item.DisplayName;
        line.PurchaseUnitDescription = item.PackagingDescription;
        if (line.UnitCost == 0) line.UnitCost = item.DefaultPurchasePrice;
        if (line.TaxRatePercent == 0) line.TaxRatePercent = item.TaxRatePercent;
    }

    public async Task SaveAsync()
    {
        if (SavedInvoice is not null) return;
        SavedSuccessfully = false;
        ErrorMessage = string.Empty;

        if (SupplierId <= 0) { ErrorMessage = "الرجاء اختيار المورد."; return; }
        if (BranchId <= 0) { ErrorMessage = "الرجاء اختيار الفرع."; return; }
        if (!Lines.Any()) { ErrorMessage = "يجب إضافة صنف واحد على الأقل."; return; }
        if (Lines.Any(l => l.ItemId <= 0)) { ErrorMessage = "الرجاء اختيار الصنف لكل سطر."; return; }
        if (Lines.Any(l => l.Quantity <= 0)) { ErrorMessage = "الكمية يجب أن تكون أكبر من صفر لكل سطر."; return; }

        IsBusy = true;
        try
        {
            var dto = new PurchaseInvoiceUpsertDto
            {
                SupplierId = SupplierId,
                GoodsReceiptNoteId = GoodsReceiptNoteId,
                BranchId = BranchId,
                InvoiceDate = InvoiceDate,
                DueDate = DueDate,
                DiscountAmount = DiscountAmount,
                PurchaseType = PurchaseType,
                Notes = Notes,
                Lines = Lines.Select(l => new PurchaseInvoiceLineUpsertDto
                {
                    ItemId = l.ItemId,
                    Quantity = l.Quantity,
                    UnitCost = l.UnitCost,
                    TaxRatePercent = l.TaxRatePercent,
                    DiscountAmount = l.DiscountAmount
                }).ToList()
            };

            var result = await _purchasingService.CreatePurchaseInvoiceAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ فاتورة الشراء.";
                return;
            }

            SavedInvoice = result.Value;
            SavedSuccessfully = true;
            RequestClose?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
