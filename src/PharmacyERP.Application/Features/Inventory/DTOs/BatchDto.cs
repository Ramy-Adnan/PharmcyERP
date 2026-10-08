using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Inventory.DTOs;

public class BatchDto
{
    public PurchasePricingType PurchaseType { get; set; }
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;

    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime ExpiryDate { get; set; }

    public int QuantityOnHand { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal? SalePriceOverride { get; set; }

    public DateTime ReceivedAtUtc { get; set; }
    public string? SupplierReference { get; set; }

    public int DaysUntilExpiry { get; set; }
    public bool IsExpired { get; set; }
}

public class ReceiveBatchDto
{
    public PurchasePricingType PurchaseType { get; set; }
    public int ItemId { get; set; }
    public int WarehouseId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public int Quantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal? SalePriceOverride { get; set; }
    public string? SupplierReference { get; set; }
}

public class StockAdjustmentDto
{
    public int BatchId { get; set; }
    /// <summary>Positive to increase, negative to decrease. Zero is rejected.</summary>
    public int QuantityDelta { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ItemStockSummaryDto
{
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UnitOfMeasureName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int TotalQuantityOnHand { get; set; }
    public int ReorderPoint { get; set; }
    public bool IsBelowReorderPoint { get; set; }
    public DateTime? NearestExpiryDate { get; set; }
}

public class ExpiringBatchDto
{
    public int BatchId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public int DaysUntilExpiry { get; set; }
    public int QuantityOnHand { get; set; }
    public bool IsExpired { get; set; }
}

public class StockTransactionDto
{
    public DateTime TransactionAtUtc { get; set; }
    public string TypeDisplay { get; set; } = string.Empty;
    public int QuantityChange { get; set; }
    public int ResultingQuantityOnHand { get; set; }
    public string? Notes { get; set; }
    public string? PerformedByUserName { get; set; }
}
