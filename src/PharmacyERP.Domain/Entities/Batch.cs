using PharmacyERP.Domain.Enums;
using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A specific received lot of an Item, scoped to one Warehouse. Tracking
/// stock at the batch level (rather than a single item-quantity number) is
/// what lets the system enforce FEFO (First-Expiry-First-Out) picking during
/// sales and raise accurate expiry alerts — both hard regulatory
/// requirements for pharmacy operations.
/// </summary>
public class Batch : AuditableEntity
{
    public PurchasePricingType PurchaseType { get; set; }

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime ExpiryDate { get; set; }

    public int QuantityOnHand { get; set; }
    public decimal? PackageSalePrice { get; set; }
    public decimal PurchasePrice { get; set; }

    /// <summary>Overrides Item.DefaultSalePrice for units sold from this specific batch, if set.</summary>
    public decimal? SalePriceOverride { get; set; }
    /// <summary>Distinguishes a configured zero price from the zero left by older receiving forms.</summary>
    public bool HasConfiguredSalePrice { get; set; }

    public DateTime ReceivedAtUtc { get; set; }
    public string? SupplierReference { get; set; }

    public ICollection<StockTransaction> StockTransactions { get; set; } = new List<StockTransaction>();

    public bool IsExpired(DateTime asOfUtc) => ExpiryDate.Date < asOfUtc.Date;
    public int DaysUntilExpiry(DateTime asOfUtc) => (ExpiryDate.Date - asOfUtc.Date).Days;
}
