using PharmacyERP.Domain.Common;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Purchasing;

public abstract class PurchasePriceRowBase : PurchasePricingViewModel
{
    private decimal _unitCost, _salePrice;

    protected PurchasePriceRowBase() => CalculateSalePriceCommand = new RelayCommand(CalculateSalePrice);

    public decimal UnitCost
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

    public decimal SalePrice { get => _salePrice; set => SetProperty(ref _salePrice, value); }
    public abstract decimal LineTotal { get; }
    public RelayCommand CalculateSalePriceCommand { get; }
    public void CalculateSalePrice() => SalePrice = SalePricePolicy.FromPurchasePrice(UnitCost, PurchaseType);
}
