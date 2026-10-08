using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Sales;

/// <summary>A single line in the POS cart before checkout.</summary>
public class POSLineRow : ViewModelBase
{
    private int _quantity = 1;
    private SaleUnitOption? _selectedSaleUnit;
    private int _availableQuantity;
    public IReadOnlyList<SaleUnitOption> SaleUnits { get; init; } = Array.Empty<SaleUnitOption>();
    public int UnitsPerPackage { get; init; } = 1;
    public decimal BaseSalePrice { get; init; }
    public decimal PackageSalePrice { get; init; }
    public SaleUnitOption? SelectedSaleUnit
    {
        get => _selectedSaleUnit;
        set
        {
            if (value is null || !SaleUnits.Contains(value) || !SetProperty(ref _selectedSaleUnit, value)) return;
            UnitPrice = value.Price ?? (value.IsPackage ? PackageSalePrice : Math.Round(BaseSalePrice * value.Factor, 2, MidpointRounding.AwayFromZero));
            OnPropertyChanged(nameof(UnitsPerSale));
            OnPropertyChanged(nameof(AvailableQuantity));
            OnPropertyChanged(nameof(SelectedUnitName));
            OnPropertyChanged(nameof(SellAsPackage));
            OnPropertyChanged(nameof(ItemSaleUnitId));
        }
    }
    public int? ItemSaleUnitId => SelectedSaleUnit?.ItemSaleUnitId;
    public int UnitsPerSale => SelectedSaleUnit?.Factor ?? 1;
    public bool SellAsPackage => SelectedSaleUnit?.IsPackage ?? false;
    public string SelectedUnitName => SelectedSaleUnit?.Name ?? UnitOfMeasureName;
    public int AvailableBaseQuantity { get => _availableQuantity; set { if (SetProperty(ref _availableQuantity, value)) OnPropertyChanged(nameof(AvailableQuantity)); } }

    private decimal _unitPrice;
    private decimal _discountAmount;

    public int ItemId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string UnitOfMeasureName { get; init; } = string.Empty;
    public decimal TaxRatePercent { get; init; }
    public int AvailableQuantity { get => AvailableBaseQuantity / UnitsPerSale; set => AvailableBaseQuantity = value * UnitsPerSale; }
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

public sealed record SaleUnitOption(bool IsPackage, string Name, int Factor, int? ItemSaleUnitId = null, string? Barcode = null, decimal? Price = null);
