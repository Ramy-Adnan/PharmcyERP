using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// The master record for a pharmacy product (a medicine, a medical supply, or
/// cosmetic item). Prices and reorder thresholds set here are the defaults;
/// actual purchase cost is tracked per-Batch since suppliers change prices
/// over time and the pharmacy needs accurate cost-of-goods-sold per batch.
/// </summary>
public class Item : AuditableEntity
{
    public PurchasePricingType PurchaseType { get; set; }

    public string Code { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Strength { get; set; }
    public ItemForm Form { get; set; } = ItemForm.Tablet;

    public int CategoryId { get; set; }
    public ItemCategory Category { get; set; } = null!;

    public int UnitOfMeasureId { get; set; }
    public UnitOfMeasure UnitOfMeasure { get; set; } = null!;

    public int? ManufacturerId { get; set; }
    public Manufacturer? Manufacturer { get; set; }

    public bool RequiresPrescription { get; set; }
    public bool IsControlledSubstance { get; set; }

    /// <summary>Default sale price; individual batches may override via Batch.SalePriceOverride.</summary>
    public decimal DefaultSalePrice { get; set; }

    /// <summary>Reference cost used before any batches exist yet (e.g. for initial reporting).</summary>
    public decimal DefaultPurchasePrice { get; set; }

    public decimal TaxRatePercent { get; set; }

    public int ReorderPoint { get; set; }
    public int MinStockLevel { get; set; }
    public int MaxStockLevel { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Batch> Batches { get; set; } = new List<Batch>();
    public ICollection<StockTransaction> StockTransactions { get; set; } = new List<StockTransaction>();
}
