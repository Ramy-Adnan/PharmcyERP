using PharmacyERP.Application.Features.Sales.DTOs;

namespace PharmacyERP.Application.Features.Sales;

/// <summary>Prices sold units against the same FEFO sequence used to issue stock.</summary>
public static class BatchSalePricing
{
    public const string PricesChangedMessage = "تغيّرت أسعار أو كميات الدفعات. راجع أسعار السلة الجديدة ثم أتم البيع مجدداً.";

    public static List<SalePriceSegmentDto> Quote(IReadOnlyList<SaleBatchPriceDto> batches, int quantity, int factor,
        Dictionary<int, int> reserved)
    {
        var result = new List<SalePriceSegmentDto>();
        if (quantity <= 0 || factor <= 0 || quantity > int.MaxValue / factor) return result;
        if (batches.Sum(b => (long)Math.Max(0, b.AvailableBaseQuantity - reserved.GetValueOrDefault(b.BatchId))) < (long)quantity * factor
            || batches.Any(b => !b.UnitPrices.ContainsKey(factor))) return result;
        var remaining = quantity;
        var partialQuantity = 0;
        var partialPrice = 0m;
        void Add(int count, decimal price)
        {
            if (result.Count > 0 && result[^1].UnitPrice == price)
                result[^1] = result[^1] with { Quantity = result[^1].Quantity + count };
            else result.Add(new(count, price));
        }
        foreach (var batch in batches)
        {
            var available = Math.Max(0, batch.AvailableBaseQuantity - reserved.GetValueOrDefault(batch.BatchId));
            var price = batch.UnitPrices[factor];
            while (available > 0 && remaining > 0)
            {
                if (partialQuantity == 0 && available >= factor)
                {
                    var count = Math.Min(remaining, available / factor);
                    var take = count * factor;
                    Add(count, price); remaining -= count; available -= take;
                    reserved[batch.BatchId] = reserved.GetValueOrDefault(batch.BatchId) + take;
                }
                else
                {
                    // One box/strip may draw stock from the end of one batch and the next.
                    var take = Math.Min(available, factor - partialQuantity);
                    partialPrice += price * take / factor;
                    partialQuantity += take; available -= take;
                    reserved[batch.BatchId] = reserved.GetValueOrDefault(batch.BatchId) + take;
                    if (partialQuantity == factor)
                    {
                        Add(1, Math.Round(partialPrice, 2, MidpointRounding.AwayFromZero));
                        remaining--; partialQuantity = 0; partialPrice = 0;
                    }
                }
            }
            if (remaining == 0) break;
        }
        return result;
    }
}
