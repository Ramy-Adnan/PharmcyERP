using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Inventory.DTOs;

public class ItemUpsertDto
{
    public PurchasePricingType PurchaseType { get; set; }

    public int? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    /// <summary>Stock is counted in the base unit; purchasing quantities/prices are per package.</summary>
    public int UnitsPerPackage { get; set; } = 1;
    public string PackageUnitName { get; set; } = "علبة";
    public string? BaseUnitBarcode { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Strength { get; set; }
    public ItemForm Form { get; set; } = ItemForm.Tablet;

    public int CategoryId { get; set; }
    public int UnitOfMeasureId { get; set; }
    public int? ManufacturerId { get; set; }

    public bool RequiresPrescription { get; set; }
    public bool IsControlledSubstance { get; set; }

    public decimal? DefaultSalePrice { get; set; }
    public decimal DefaultPurchasePrice { get; set; }
    public decimal TaxRatePercent { get; set; }

    public int ReorderPoint { get; set; }
    public int MinStockLevel { get; set; }
    public int MaxStockLevel { get; set; }
    public bool IsActive { get; set; } = true;
}
