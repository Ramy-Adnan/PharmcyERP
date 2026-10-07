using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

/// <summary>A single editable line in the Purchase Invoice editor grid.</summary>
public class PILineRow : ViewModelBase
{
    private int _itemId;
    private string _itemCode = string.Empty;
    private string _itemName = string.Empty;
    private int _quantity = 1;
    private decimal _unitCost;
    private decimal _taxRatePercent;
    private decimal _discountAmount;

    public int ItemId { get => _itemId; set => SetProperty(ref _itemId, value); }
    public string ItemCode { get => _itemCode; set => SetProperty(ref _itemCode, value); }
    public string ItemName { get => _itemName; set => SetProperty(ref _itemName, value); }

    public int Quantity
    {
        get => _quantity;
        set { if (SetProperty(ref _quantity, value)) OnPropertyChanged(nameof(LineTotal)); }
    }

    public decimal UnitCost
    {
        get => _unitCost;
        set { if (SetProperty(ref _unitCost, value)) OnPropertyChanged(nameof(LineTotal)); }
    }

    public decimal TaxRatePercent
    {
        get => _taxRatePercent;
        set { if (SetProperty(ref _taxRatePercent, value)) OnPropertyChanged(nameof(LineTotal)); }
    }

    public decimal DiscountAmount
    {
        get => _discountAmount;
        set { if (SetProperty(ref _discountAmount, value)) OnPropertyChanged(nameof(LineTotal)); }
    }

    public decimal LineTotal => Math.Round(UnitCost * Quantity * (1 + TaxRatePercent / 100m) - DiscountAmount, 2);
}
