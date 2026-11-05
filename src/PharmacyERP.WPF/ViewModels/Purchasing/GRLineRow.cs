using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

/// <summary>A single editable line in the Goods Receipt editor grid.</summary>
public class GRLineRow : PurchasePriceRowBase
{
    private int _itemId;
    private string _itemCode = string.Empty;
    private string _itemName = string.Empty;
    private string _batchNumber = string.Empty;
    private DateTime? _manufactureDate;
    private DateTime? _expiryDate;
    private int _quantityReceived = 1;
    private string _entryIssue = "اختر الصنف.";

    public int? Id { get; set; }
    public int? PurchaseOrderItemId { get; set; }

    private int _bonusQuantity;
    public int BonusQuantity { get => _bonusQuantity; set { if (SetProperty(ref _bonusQuantity, value)) OnPropertyChanged(nameof(StockQuantity)); } }
    public int ItemId { get => _itemId; set => SetProperty(ref _itemId, value); }
    public string ItemCode { get => _itemCode; set => SetProperty(ref _itemCode, value); }
    public string ItemName { get => _itemName; set => SetProperty(ref _itemName, value); }
    public string BatchNumber { get => _batchNumber; set => SetProperty(ref _batchNumber, value); }
    public DateTime? ManufactureDate { get => _manufactureDate; set => SetProperty(ref _manufactureDate, value); }
    public DateTime? ExpiryDate { get => _expiryDate; set => SetProperty(ref _expiryDate, value); }

    public int QuantityReceived
    {
        get => _quantityReceived;
        set { if (SetProperty(ref _quantityReceived, value)) { OnPropertyChanged(nameof(LineTotal)); OnPropertyChanged(nameof(StockQuantity)); } }
    }

    public override decimal LineTotal => Math.Round(UnitCost * QuantityReceived, 2);
    public long StockQuantity => ((long)QuantityReceived + BonusQuantity) * PurchaseUnitFactor;
    public string EntryIssue { get => _entryIssue; private set { if (SetProperty(ref _entryIssue, value)) OnPropertyChanged(nameof(NeedsAttention)); } }
    public bool NeedsAttention => !string.IsNullOrEmpty(EntryIssue);

    protected override void ConvertUnitPrices(int previousFactor, int nextFactor)
    {
        base.ConvertUnitPrices(previousFactor, nextFactor);
        OnPropertyChanged(nameof(StockQuantity));
    }

    public string? FirstIncompleteField(DateTime receiptDate)
    {
        if (ItemId <= 0) return nameof(ItemId);
        if (string.IsNullOrWhiteSpace(BatchNumber)) return nameof(BatchNumber);
        if (!ExpiryDate.HasValue || ExpiryDate.Value.Date <= receiptDate.Date) return nameof(ExpiryDate);
        if (QuantityReceived < 0 || BonusQuantity < 0 || (long)QuantityReceived + BonusQuantity <= 0 || StockQuantity > int.MaxValue)
            return nameof(QuantityReceived);
        if (UnitCost < 0) return nameof(UnitCost);
        if (SalePrice < 0) return nameof(SalePrice);
        return null;
    }

    public void RefreshEntryStatus(DateTime receiptDate) => EntryIssue = FirstIncompleteField(receiptDate) switch
    {
        nameof(ItemId) => "اختر الصنف.",
        nameof(BatchNumber) => "أدخل رقم الدفعة.",
        nameof(ExpiryDate) => "أدخل تاريخ الصلاحية بعد تاريخ الاستلام.",
        nameof(QuantityReceived) => "راجع الكمية والبونص؛ الوارد للمخزن يجب أن يكون موجباً وضمن الحد المسموح.",
        nameof(UnitCost) => "سعر الشراء لا يقبل قيمة سالبة.",
        nameof(SalePrice) => "سعر البيع لا يقبل قيمة سالبة.",
        _ => string.Empty
    };
}
