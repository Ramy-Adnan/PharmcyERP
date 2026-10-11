using PharmacyERP.WPF.MVVM;
using PharmacyERP.Application.Features.Sales.DTOs;

namespace PharmacyERP.WPF.ViewModels.Sales;

/// <summary>A single line in the POS cart before checkout.</summary>
public class POSLineRow : ViewModelBase
{
    private string? _photoPath;
    public string? PhotoPath { get => _photoPath; set { _photoPath = value; OnPropertyChanged(); } }
    private int _quantity = 1;
    private SaleUnitOption? _selectedSaleUnit;
    private int _availableQuantity;
    private bool _manualPrice, _applyingPrice;
    public bool UsesBatchPricing => !_manualPrice;
    public IReadOnlyList<SalePriceSegmentDto> PriceSegments { get; private set; } = Array.Empty<SalePriceSegmentDto>();
    public bool HasMultipleBatchPrices => UsesBatchPricing && PriceSegments.Count > 1;
    public bool HasCompletePriceQuote => PriceSegments.Sum(p => p.Quantity) == Quantity;
    public bool HasZeroSalePrice => UsesBatchPricing && HasCompletePriceQuote ? PriceSegments.Any(p => p.UnitPrice == 0) : UnitPrice == 0;
    public string UnitPriceDisplay => HasMultipleBatchPrices ? "متعدد" : UnitPrice.ToString("N2");
    public string PriceBreakdown => string.Join(" + ", PriceSegments.Select(p => $"{p.Quantity} × {p.UnitPrice:N2}"));
    public decimal SubTotal => UsesBatchPricing && HasCompletePriceQuote ? PriceSegments.Sum(p => p.Quantity * p.UnitPrice) : UnitPrice * Quantity;

    public void UseAutomaticPricing() { _manualPrice = false; OnPropertyChanged(nameof(UsesBatchPricing)); }
    public void ApplyPriceQuote(IReadOnlyList<SalePriceSegmentDto> prices)
    {
        PriceSegments = prices;
        _applyingPrice = true;
        try
        {
            if (UsesBatchPricing && HasCompletePriceQuote && Quantity > 0)
                UnitPrice = Math.Round(prices.Sum(p => p.Quantity * p.UnitPrice) / Quantity, 2, MidpointRounding.AwayFromZero);
        }
        finally { _applyingPrice = false; }
        foreach (var property in new[] { nameof(SubTotal), nameof(LineTotal), nameof(HasMultipleBatchPrices), nameof(UnitPriceDisplay), nameof(PriceBreakdown) })
            OnPropertyChanged(property);
    }
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
            _manualPrice = false; _applyingPrice = true;
            try { UnitPrice = value.Price ?? (value.IsPackage ? PackageSalePrice : Math.Round(BaseSalePrice * value.Factor, 2, MidpointRounding.AwayFromZero)); }
            finally { _applyingPrice = false; }
            OnPropertyChanged(nameof(UsesBatchPricing));
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
        set
        {
            if (!_applyingPrice) _manualPrice = true;
            if (SetProperty(ref _unitPrice, value)) { OnPropertyChanged(nameof(SubTotal)); OnPropertyChanged(nameof(LineTotal)); }
            if (!_applyingPrice) OnPropertyChanged(nameof(UsesBatchPricing));
        }
    }

    public decimal DiscountAmount
    {
        get => _discountAmount;
        set { if (SetProperty(ref _discountAmount, value)) OnPropertyChanged(nameof(LineTotal)); }
    }

    public decimal LineTotal => Math.Round(SubTotal * (1 + TaxRatePercent / 100m) - DiscountAmount, 2);
}

public sealed record SaleUnitOption(bool IsPackage, string Name, int Factor, int? ItemSaleUnitId = null, string? Barcode = null, decimal? Price = null);
