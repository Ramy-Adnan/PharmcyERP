namespace PharmacyERP.Domain.Common;

public static class SalePricePolicy
{
    public static decimal FromPurchasePrice(decimal purchasePrice) =>
        Math.Round(purchasePrice * 1.03m, 2, MidpointRounding.AwayFromZero);
}
