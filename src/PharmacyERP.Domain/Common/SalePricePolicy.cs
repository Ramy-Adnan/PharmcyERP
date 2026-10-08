using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Common;

public static class SalePricePolicy
{
    public static decimal MarkupPercent(PurchasePricingType purchaseType) => purchaseType switch
    {
        PurchasePricingType.ByHand => 20m,
        PurchasePricingType.Other => 25m,
        _ => throw new ArgumentOutOfRangeException(nameof(purchaseType))
    };

    public static decimal FromPurchasePrice(decimal purchasePrice, PurchasePricingType purchaseType = PurchasePricingType.Other) =>
        Math.Round(purchasePrice * (1 + MarkupPercent(purchaseType) / 100m), 2, MidpointRounding.AwayFromZero);
}
