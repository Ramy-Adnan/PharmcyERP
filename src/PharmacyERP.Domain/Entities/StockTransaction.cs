using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// Immutable ledger entry recording every single change to a Batch's
/// quantity. QuantityChange is signed (positive for increases, negative for
/// decreases) so the full stock history — and the running balance at any
/// point in time — can always be reconstructed for audits and for
/// controlled-substance compliance reporting.
/// </summary>
public class StockTransaction : BaseEntity
{
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public StockTransactionType Type { get; set; }
    public int QuantityChange { get; set; }
    public int ResultingQuantityOnHand { get; set; }

    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? Notes { get; set; }

    public DateTime TransactionAtUtc { get; set; } = DateTime.UtcNow;
    public int? PerformedByUserId { get; set; }
}
