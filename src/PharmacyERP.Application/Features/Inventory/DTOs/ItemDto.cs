using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Inventory.DTOs;

public class ItemDto
{
    public PurchasePricingType PurchaseType { get; set; }

    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    /// <summary>Stock is counted in the base unit; purchasing quantities/prices are per package.</summary>
    public List<ItemSaleUnitDto> SaleUnits { get; set; } = new();
    public int UnitsPerPackage { get; set; } = 1;
    public string PackageUnitName { get; set; } = "علبة";
    public string? BaseUnitBarcode { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Strength { get; set; }
    public ItemForm Form { get; set; }

    public int CategoryId { get; set; }
    public int UnitOfMeasureId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string UnitOfMeasureName { get; set; } = string.Empty;
    public string? ManufacturerName { get; set; }

    public bool RequiresPrescription { get; set; }
    public bool IsControlledSubstance { get; set; }

    public decimal DefaultSalePrice { get; set; }
    public decimal DefaultPurchasePrice { get; set; }
    public decimal TaxRatePercent { get; set; }

    public int ReorderPoint { get; set; }
    public int MinStockLevel { get; set; }
    public int MaxStockLevel { get; set; }
    public bool IsActive { get; set; }

    public IReadOnlyList<PurchaseUnitOption> PurchaseUnits => new[] { new PurchaseUnitOption(null, PurchaseUnitName, UnitsPerPackage) }
        .Concat(SaleUnits.Where(u => u.IsActive).Select(u => new PurchaseUnitOption(u.Id, u.Name, u.BaseUnitCount))).ToList();
    public string DisplayName => string.Join(" · ", new[] { Name, Strength, ManufacturerName }.Where(v => !string.IsNullOrWhiteSpace(v)));
    public string PurchaseUnitName => UnitsPerPackage > 1 ? PackageUnitName : UnitOfMeasureName;
    public string PackagingDescription => $"{PurchaseUnitName} = {UnitsPerPackage} {UnitOfMeasureName}";

    public int TotalQuantityOnHand { get; set; }
}
