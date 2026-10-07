using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

/// <summary>A single editable line in the Purchase Order editor grid, bound directly to DataGrid cells.</summary>
public class POLineRow : ViewModelBase
{
    private int _itemId;
    private string _itemCode = string.Empty;
    private string _itemName = string.Empty;
    private int _quantityOrdered = 1;
    private decimal _unitCost;
    private decimal _taxRatePercent;

    public int? Id { get; set; }

    public int ItemId { get => _itemId; set => SetProperty(ref _itemId, value); }
    public string ItemCode { get => _itemCode; set => SetProperty(ref _itemCode, value); }
    public string ItemName { get => _itemName; set => SetProperty(ref _itemName, value); }

    public int QuantityOrdered
    {
        get => _quantityOrdered;
        set { if (SetProperty(ref _quantityOrdered, value)) OnPropertyChanged(nameof(LineTotal)); }
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

    public decimal LineTotal => Math.Round(UnitCost * QuantityOrdered * (1 + TaxRatePercent / 100m), 2);
}
