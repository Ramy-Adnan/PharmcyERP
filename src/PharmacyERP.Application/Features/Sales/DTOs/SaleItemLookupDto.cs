namespace PharmacyERP.Application.Features.Sales.DTOs;

/// <summary>Fast lookup result used by the POS screen when a cashier scans a barcode or searches by name/code.</summary>
public class SaleItemLookupDto
{
    public int ItemId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    /// <summary>Stock is counted in the base unit; purchasing quantities/prices are per package.</summary>
    public int UnitsPerPackage { get; set; } = 1;
    public string PackageUnitName { get; set; } = "علبة";
    public string? BaseUnitBarcode { get; set; }

    public string Name { get; set; } = string.Empty;
    public string UnitOfMeasureName { get; set; } = string.Empty;
    public string? ManufacturerName { get; set; }
    public string? Strength { get; set; }
    public string DisplayName => string.Join(" · ", new[] { Name, Strength, ManufacturerName }.Where(v => !string.IsNullOrWhiteSpace(v)));
    public decimal PackageSalePrice { get; set; }
    public string StockDisplay => $"{AvailableQuantity} {UnitOfMeasureName}";
    public decimal DefaultSalePrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public bool RequiresPrescription { get; set; }
    public int AvailableQuantity { get; set; }
}
