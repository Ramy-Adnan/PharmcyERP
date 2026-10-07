using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Inventory.DTOs;

namespace PharmacyERP.Application.Features.Inventory;

/// <summary>
/// Full Inventory module surface: lookup CRUD (categories/units/manufacturers),
/// item master CRUD, batch receiving, stock adjustments, and the read models
/// used for stock overview / low-stock / expiry-alert screens. Every quantity
/// change funnels through RecordStockTransactionAsync internally so the
/// StockTransaction ledger is always complete and consistent with Batch.QuantityOnHand.
/// </summary>
public interface IInventoryService
{
    // Categories
    Task<List<ItemCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<Result<ItemCategoryDto>> UpsertCategoryAsync(int? id, string code, string name, bool isActive, CancellationToken cancellationToken = default);
    Task<Result> DeleteCategoryAsync(int id, CancellationToken cancellationToken = default);

    // Units of Measure
    Task<List<UnitOfMeasureDto>> GetUnitsAsync(CancellationToken cancellationToken = default);
    Task<Result<UnitOfMeasureDto>> UpsertUnitAsync(int? id, string code, string name, bool isActive, CancellationToken cancellationToken = default);
    Task<Result> DeleteUnitAsync(int id, CancellationToken cancellationToken = default);

    // Manufacturers
    Task<List<ManufacturerDto>> GetManufacturersAsync(CancellationToken cancellationToken = default);
    Task<Result<ManufacturerDto>> UpsertManufacturerAsync(int? id, string name, string? country, bool isActive, CancellationToken cancellationToken = default);
    Task<Result> DeleteManufacturerAsync(int id, CancellationToken cancellationToken = default);

    // Items
    Task<List<ItemDto>> GetItemsAsync(string? searchText = null, CancellationToken cancellationToken = default);
    Task<ItemUpsertDto?> GetItemForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<ItemDto>> CreateItemAsync(ItemUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<ItemDto>> UpdateItemAsync(ItemUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SetItemActiveStatusAsync(int itemId, bool isActive, CancellationToken cancellationToken = default);

    // Batches / Stock movement
    Task<List<BatchDto>> GetBatchesForItemAsync(int itemId, int? warehouseId = null, CancellationToken cancellationToken = default);
    Task<Result<BatchDto>> ReceiveBatchAsync(ReceiveBatchDto dto, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<Result> AdjustStockAsync(StockAdjustmentDto dto, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<List<StockTransactionDto>> GetTransactionHistoryAsync(int batchId, CancellationToken cancellationToken = default);

    // Reporting / overview screens
    Task<List<ItemStockSummaryDto>> GetStockOverviewAsync(int warehouseId, bool lowStockOnly = false, CancellationToken cancellationToken = default);
    Task<List<ExpiringBatchDto>> GetExpiringBatchesAsync(int warehouseId, int withinDays = 90, CancellationToken cancellationToken = default);

    /// <summary>Total sellable quantity on hand for an item in a warehouse, summed across all its batches. Used by the POS screen to show live stock before adding a line.</summary>
    Task<int> GetAvailableQuantityAsync(int itemId, int warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deducts the requested quantity from the item's batches in a warehouse using
    /// FEFO (First-Expiry-First-Out) picking, writing one SaleIssue StockTransaction
    /// per batch touched. Fails atomically (no partial deduction) if total stock on
    /// hand is insufficient. This is the single integration point the Sales module
    /// uses to touch stock — it never edits Batch.QuantityOnHand directly.
    /// </summary>
    Task<Result<List<BatchAllocationResultDto>>> IssueStockFefoAsync(
        int itemId, int warehouseId, int quantity, string referenceType, int? referenceId,
        int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Reverses a prior FEFO issuance for a customer return: adds the quantity back to the specific batch it was originally taken from.</summary>
    Task<Result> RestockBatchAsync(
        int batchId, int quantity, string referenceType, int? referenceId,
        int? performedByUserId, CancellationToken cancellationToken = default);
}
