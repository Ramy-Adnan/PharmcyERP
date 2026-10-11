namespace PharmacyERP.Application.Features.Sales.DTOs;

/// <summary>A valid batch in FEFO order, with prices for each supported base-unit factor.</summary>
public class SaleBatchPriceDto
{
    public int BatchId { get; set; }
    public int AvailableBaseQuantity { get; set; }
    public Dictionary<int, decimal> UnitPrices { get; set; } = new();
}

public sealed record SalePriceSegmentDto(int Quantity, decimal UnitPrice);
