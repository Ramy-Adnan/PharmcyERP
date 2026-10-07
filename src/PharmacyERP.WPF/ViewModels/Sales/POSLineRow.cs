using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Sales;

/// <summary>A single line in the POS cart before checkout.</summary>
public class POSLineRow : ViewModelBase
{
    private int _quantity = 1;
    private decimal _unitPrice;
    private decimal _discountAmount;

    public int ItemId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string UnitOfMeasureName { get; init; } = string.Empty;
    public decimal TaxRatePercent { get; init; }
    public int AvailableQuantity { get; init; }
    public bool RequiresPrescription { get; init; }

    public int Quantity
    {
        get => _quantity;
        set { if (SetProperty(ref _quantity, value)) OnPropertyChanged(nameof(LineTotal)); }
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set { if (SetProperty(ref _unitPrice, value)) OnPropertyChanged(nameof(LineTotal)); }
    }

    public decimal DiscountAmount
    {
        get => _discountAmount;
        set { if (SetProperty(ref _discountAmount, value)) OnPropertyChanged(nameof(LineTotal)); }
    }

    public decimal LineTotal => Math.Round(UnitPrice * Quantity * (1 + TaxRatePercent / 100m) - DiscountAmount, 2);
}
