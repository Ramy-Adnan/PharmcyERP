using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// Records exactly which Batch a portion of a sold line's quantity was taken
/// from, and at what cost (for margin reporting). This is what makes FEFO
/// selling auditable and what SalesReturns replay in reverse to restock the
/// correct batches rather than an arbitrary one.
/// </summary>
public class SalesInvoiceItemBatch : BaseEntity
{
    public int SalesInvoiceItemId { get; set; }
    public SalesInvoiceItem SalesInvoiceItem { get; set; } = null!;

    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    public int QuantityTaken { get; set; }
    public decimal UnitCost { get; set; }

    /// <summary>
    /// How much of this specific allocation has already been restocked via a
    /// customer return. Tracked per-allocation (not just per-line) so that
    /// repeated partial returns against the same sold line always restock the
    /// correct remaining amount from the correct batch, even when the original
    /// line quantity was split across more than one batch by FEFO.
    /// </summary>
    public int RestockedQuantity { get; set; }

    public int QuantityAvailableToReturn => QuantityTaken - RestockedQuantity;
}
