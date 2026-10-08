using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels;

public record PurchaseTypeOption(PurchasePricingType Value, string Name);

public abstract class PurchasePricingViewModel : ViewModelBase
{
    private PurchasePricingType _purchaseType;

    public IReadOnlyList<PurchaseTypeOption> PurchaseTypes { get; } = new[]
    {
        new PurchaseTypeOption(PurchasePricingType.Other, "باقي المشتريات — 25%"),
        new PurchaseTypeOption(PurchasePricingType.ByHand, "باي هاند — 20%")
    };

    public PurchasePricingType PurchaseType
    {
        get => _purchaseType;
        set
        {
            if (!SetProperty(ref _purchaseType, value)) return;
            OnPropertyChanged(nameof(SalePriceLabel));
            OnPropertyChanged(nameof(RecalculatePriceLabel));
            OnPropertyChanged(nameof(MarkupLabel));
            OnPurchaseTypeChanged();
        }
    }

    public string MarkupLabel => $"+{SalePricePolicy.MarkupPercent(PurchaseType):0}%";
    public string SalePriceLabel => $"سعر البيع (الشراء {MarkupLabel})";
    public string RecalculatePriceLabel => $"إعادة حساب {MarkupLabel}";
    protected virtual void OnPurchaseTypeChanged() { }
}
