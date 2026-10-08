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
    private DateTime _expiryDate = DateTime.Today.AddYears(1);
    private int _quantityReceived = 1;

    public int? Id { get; set; }
    public int? PurchaseOrderItemId { get; set; }

    public string PurchaseUnitDescription { get; set; } = string.Empty;
    public int ItemId { get => _itemId; set => SetProperty(ref _itemId, value); }
    public string ItemCode { get => _itemCode; set => SetProperty(ref _itemCode, value); }
    public string ItemName { get => _itemName; set => SetProperty(ref _itemName, value); }
    public string BatchNumber { get => _batchNumber; set => SetProperty(ref _batchNumber, value); }
    public DateTime? ManufactureDate { get => _manufactureDate; set => SetProperty(ref _manufactureDate, value); }
    public DateTime ExpiryDate { get => _expiryDate; set => SetProperty(ref _expiryDate, value); }

    public int QuantityReceived
    {
        get => _quantityReceived;
        set { if (SetProperty(ref _quantityReceived, value)) OnPropertyChanged(nameof(LineTotal)); }
    }

    public override decimal LineTotal => Math.Round(UnitCost * QuantityReceived, 2);
}
