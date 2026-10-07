namespace PharmacyERP.Application.Features.Sales.DTOs;

/// <summary>Fast lookup result used by the POS screen when a cashier scans a barcode or searches by name/code.</summary>
public class SaleItemLookupDto
{
    public int ItemId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UnitOfMeasureName { get; set; } = string.Empty;
    public decimal DefaultSalePrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool RequiresPrescription { get; set; }
    public int AvailableQuantity { get; set; }
}
