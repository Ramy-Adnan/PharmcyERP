using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Reports.DTOs;

public class InventoryMovementLineDto
{
    public DateTime TransactionAtUtc { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public StockTransactionType Type { get; set; }
    public int QuantityChange { get; set; }
    public int ResultingQuantityOnHand { get; set; }
    public string? ReferenceType { get; set; }
    public string? Notes { get; set; }
}
