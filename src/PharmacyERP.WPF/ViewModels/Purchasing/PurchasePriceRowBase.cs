using PharmacyERP.Domain.Common;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

public abstract class PurchasePriceRowBase : PurchaseUnitRowBase
{
    private decimal _unitCost, _salePrice;
    private bool _manualSalePrice;

    protected PurchasePriceRowBase() => CalculateSalePriceCommand = new RelayCommand(CalculateSalePrice);

    public override decimal UnitCost
    {
        get => _unitCost;
        set
        {
            if (!SetProperty(ref _unitCost, value)) return;
            CalculateSalePrice();
            OnPropertyChanged(nameof(LineTotal));
        }
    }

    protected override void OnPurchaseTypeChanged() => CalculateSalePrice();

    protected override void ConvertUnitPrices(int previousFactor, int nextFactor)
    {
        // Changing a unit is a change of representation. Keep a deliberately
        // edited selling price instead of replacing it with the default markup.
        var preserveManualPrice = _manualSalePrice;
        var convertedSalePrice = Math.Round(SalePrice / previousFactor * nextFactor, 2, MidpointRounding.AwayFromZero);
        base.ConvertUnitPrices(previousFactor, nextFactor);
        if (preserveManualPrice) SalePrice = convertedSalePrice;
    }

    public decimal SalePrice
    {
        get => _salePrice;
        set
        {
            _manualSalePrice = value != SalePricePolicy.FromPurchasePrice(UnitCost, PurchaseType);
            SetProperty(ref _salePrice, value);
        }
    }
    public abstract decimal LineTotal { get; }
    public RelayCommand CalculateSalePriceCommand { get; }
    public void CalculateSalePrice() => SalePrice = SalePricePolicy.FromPurchasePrice(UnitCost, PurchaseType);
}
