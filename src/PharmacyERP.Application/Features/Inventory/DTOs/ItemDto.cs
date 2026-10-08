using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Inventory.DTOs;

public class ItemDto
{
    public PurchasePricingType PurchaseType { get; set; }

    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Strength { get; set; }
    public ItemForm Form { get; set; }

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

    public int TotalQuantityOnHand { get; set; }
}
