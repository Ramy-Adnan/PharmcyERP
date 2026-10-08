using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;
namespace PharmacyERP.WPF.ViewModels.Purchasing;
public sealed class InvoiceImageImportViewModel : ViewModelBase
{
    private readonly IInvoiceImageReader _reader;
    private readonly IPurchaseImageImportService _import;
    private readonly IInventoryService _inventory;
    private readonly PharmacyERP.Application.Common.Interfaces.ICurrentUserService? _user;
    public byte[]? OriginalPhoto { get; private set; }
    public bool CanManageUnits => _user?.HasPermission("Inventory.ManageLookups") == true;
    private int _supplierId, _branchId, _warehouseId;
    private Guid _requestId;
    private InvoiceImageDocument? _document;
    private CancellationTokenSource? _reading;
    public InvoiceImageImportViewModel(IInvoiceImageReader reader, IPurchaseImageImportService import, IInventoryService inventory, PharmacyERP.Application.Common.Interfaces.ICurrentUserService? user = null) { _reader = reader; _import = import; _inventory = inventory; _user = user; }
    public async Task InitializeAsync(int supplierId, int branchId, int warehouseId)
    {
        _supplierId = supplierId; _branchId = branchId; _warehouseId = warehouseId;
        Items = (await _inventory.GetItemsAsync()).Where(i => i.IsActive).ToList();
        Units = (await _inventory.GetUnitsAsync()).Where(u => u.IsActive).ToList();
        Categories = (await _inventory.GetCategoriesAsync()).Where(c => c.IsActive).ToList();
    }
    public IReadOnlyList<ItemDto> Items { get; private set; } = Array.Empty<ItemDto>();
    public IReadOnlyList<UnitOfMeasureDto> Units { get; private set; } = Array.Empty<UnitOfMeasureDto>();
    public IReadOnlyList<ItemCategoryDto> Categories { get; private set; } = Array.Empty<ItemCategoryDto>();
    public ObservableCollection<InvoiceImageReviewRow> Lines { get; } = new();
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanSave)); } } }
    public bool CanEdit => !IsBusy;
    public bool CanSave => !IsBusy && _document is not null;
    private string _message = "اختر صورة فاتورة واحدة واضحة. تُرسل الصورة إلى OpenAI للتحليل، ثم تراجع النتائج قبل الاستلام. تحتاج خدمة API مفعّلة.";
    public string Message { get => _message; set => SetProperty(ref _message, value); }
    public string PhotoInformation => _document is null ? "" : $"{_document.SourceFileName} · المورد المقروء: {_document.SupplierName} · العملة: {_document.Currency} · {_document.Notes}";
    public int? SavedReceiptId { get; private set; }
    private string _invoiceNumber = "";
    public string InvoiceNumber { get => _invoiceNumber; set => SetProperty(ref _invoiceNumber, value); }
    private DateTime? _invoiceDate;
    public DateTime? InvoiceDate { get => _invoiceDate; set => SetProperty(ref _invoiceDate, value); }
    private decimal? _invoiceTotal;
    public decimal? InvoiceTotal { get => _invoiceTotal; set => SetProperty(ref _invoiceTotal, value); }
    private PurchasePricingType _purchaseType;
    public PurchasePricingType PurchaseType { get => _purchaseType; set { if (SetProperty(ref _purchaseType, value)) foreach (var row in Lines) row.PurchaseType = value; } }
    public bool IsByHand { get => PurchaseType == PurchasePricingType.ByHand; set { PurchaseType = value ? PurchasePricingType.ByHand : PurchasePricingType.Other; } }
    public decimal ComputedTotal => Lines.Sum(l => l.LineTotal ?? 0);
    public async Task AnalyzeAsync(InvoiceImageInput input)
    {
        if (IsBusy) return;
        IsBusy = true; Message = "جارٍ قراءة الصورة ومطابقة الأصناف..."; _reading = new();
        try
        {
            var result = await _reader.ReadAsync(input, _reading.Token);
            if (!result.Succeeded) { Message = result.Errors.First(); return; }
            var doc = result.Value!;
            var matches = await _import.MatchAsync(_supplierId, doc.Lines, _reading.Token);
            _document = doc; OriginalPhoto = input.Content; _requestId = Guid.NewGuid(); Lines.Clear(); InvoiceNumber = doc.InvoiceNumber ?? ""; InvoiceDate = doc.InvoiceDate; InvoiceTotal = doc.InvoiceTotal;
            for (var index = 0; index < doc.Lines.Count; index++)
            {
                var line = doc.Lines[index]; var match = matches[index];
                var row = new InvoiceImageReviewRow { SourceName = line.Name, MatchHint = match.Reason, Candidates = match.Candidates, Items = Items, Units = Units, Categories = Categories,
                    ItemName = line.Name, Barcode = line.Barcode, Strength = line.Strength, Quantity = line.Quantity, BonusQuantity = line.BonusQuantity,
                    UnitCost = line.UnitPrice, PurchaseType = PurchaseType, PrintedLineTotal = line.LineTotal, BatchNumber = line.BatchNumber ?? "", ExpiryDate = line.ExpiryDate, ReadingNotes = line.Notes };
                row.ExistingItemId = match.ItemId;
                var kind = (line.DeclaredUnitKind ?? "").ToLowerInvariant();
                var baseName = kind.Contains("tab") || kind.Contains("tablet") ? "حبة" : kind.Contains("amp") ? "أمبولة" : kind.Contains("sachet") ? "كيس" : kind.Contains("bottle") ? "قنينة" : null;
                if (row.IsNew && baseName is not null) row.BaseUnitOfMeasureId = Units.FirstOrDefault(u => u.Name == baseName)?.Id ?? 0;
                var selectedBase = Units.FirstOrDefault(u => u.Id == row.BaseUnitOfMeasureId)?.Name;
                if (baseName is not null && selectedBase == baseName) row.BaseUnitsPerReceiveUnit = line.DeclaredUnitCount;
                row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ComputedTotal)); Lines.Add(row);
            }
            OnPropertyChanged(nameof(PhotoInformation)); OnPropertyChanged(nameof(ComputedTotal));
            Message = "راجع كل سطر وحدّد وحدة المخزون وعدد الأجزاء والدفعة والصلاحية، ثم ضع علامة «راجعت». لا تعني 20 Tab عشرين شريطاً. البونص يزيد المخزون فقط. المطابقة المحتملة تحتاج اختياراً صريحاً. المورد المستخدم هو الذي اخترته في مسار المشتريات.";
        }
        catch (OperationCanceledException) { Message = "أُلغيت القراءة. لم يُضف مخزون."; }
        catch (Exception) { Message = "تعذرت قراءة الصورة أو المطابقة؛ لم يُعتمد استلام. حاول مجدداً."; }
        finally { _reading.Dispose(); _reading = null; IsBusy = false; }
    }
    public async Task AddBaseUnitAsync(string name)
    {
        if (IsBusy || !CanManageUnits) { Message = "إضافة وحدة مخزون تحتاج صلاحية إدارة التصنيفات والوحدات."; return; }
        name = name.Trim();
        if (name.Length == 0 || name.Length > 50) { Message = "اسم الوحدة من 1 إلى 50 حرفاً."; return; }
        IsBusy = true;
        try
        {
            var all = await _inventory.GetUnitsAsync();
            var existing = all.FirstOrDefault(u => u.Name == name && u.IsActive);
            if (existing is null)
            {
                var result = await _inventory.UpsertUnitAsync(null, "IMG-" + Guid.NewGuid().ToString("N")[..12], name, true);
                if (!result.Succeeded) { Message = result.Errors.First(); return; }
                all.Add(result.Value!);
            }
            Units = all.Where(u => u.IsActive).ToList();
            foreach (var row in Lines) row.Units = Units;
            Message = "الوحدة متاحة الآن؛ اخترها للعلاج الجديد ثم أدخل عدد الأجزاء داخل وحدة الاستلام.";
        }
        catch (Exception) { Message = "تعذر إضافة الوحدة. راجع الصلاحيات والاتصال."; }
        finally { IsBusy = false; }
    }
    public void CancelReading() => _reading?.Cancel();
    public async Task<bool> SaveAsync()
    {
        if (!CanSave || _document is null) return false;
        if (!InvoiceDate.HasValue || !InvoiceTotal.HasValue) { Message = "راجع تاريخ الفاتورة وإجماليها."; return false; }
        if (Lines.Any(l => !l.Quantity.HasValue || !l.BonusQuantity.HasValue || !l.UnitCost.HasValue || !l.SalePrice.HasValue || !l.BaseUnitsPerReceiveUnit.HasValue || !l.ExpiryDate.HasValue)) { Message = "أكمل الحقول الناقصة؛ لا نخمن الكمية أو التجزئة أو الصلاحية."; return false; }
        IsBusy = true;
        try
        {
            var request = new PurchaseImageImportRequest { RequestId = _requestId, SupplierId = _supplierId, BranchId = _branchId, WarehouseId = _warehouseId,
                SupplierInvoiceNumber = InvoiceNumber, InvoiceDate = InvoiceDate.Value, SourceHash = _document.SourceHash, ParsedTotal = _document.InvoiceTotal, ReviewedTotal = InvoiceTotal.Value,
                PurchaseType = PurchaseType, Lines = Lines.Select(l => l.ToInput()).ToList() };
            var result = await _import.SaveReviewedAsync(request);
            if (!result.Succeeded) { Message = result.Errors.First(); return false; }
            SavedReceiptId = result.Value!.GoodsReceiptId;
            Message = result.Value.AlreadyImported ? "هذه الفاتورة مستوردة سابقاً؛ فتحنا سندها دون تكرارها." : "حُفظت المراجعة كسند استلام مسودة؛ أكمل مسار المشتريات لاعتماد المخزون والفاتورة والسداد.";
            return true;
        }
        catch (Exception) { Message = "تعذر حفظ الاستيراد. يمكنك المحاولة مجدداً بنفس الفاتورة؛ يمنع النظام تكرارها."; return false; }
        finally { IsBusy = false; }
    }
}
