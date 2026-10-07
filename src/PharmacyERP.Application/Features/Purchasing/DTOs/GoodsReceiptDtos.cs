using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Purchasing.DTOs;

public class GoodsReceiptNoteDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? PurchaseOrderNumber { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public GoodsReceiptStatus Status { get; set; }
    public string? Notes { get; set; }
    public decimal TotalCost { get; set; }
    public int LineCount { get; set; }
}

public class GoodsReceiptLineDto
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public int QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal SalePrice { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>
/// Creating a GoodsReceiptNote always starts it in Draft status so it can be
/// reviewed/edited before PostGoodsReceiptAsync commits it to Inventory.
/// </summary>
public class GoodsReceiptUpsertDto
{
    public int? Id { get; set; }
    public int? PurchaseOrderId { get; set; }
    public int SupplierId { get; set; }
    public int BranchId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime ReceiptDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
    public List<GoodsReceiptLineUpsertDto> Lines { get; set; } = new();
}

public class GoodsReceiptLineUpsertDto
{
    public int? Id { get; set; }
    public int? PurchaseOrderItemId { get; set; }
    public int ItemId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public int QuantityReceived { get; set; }
    public decimal UnitCost { get; set; }
    public decimal SalePrice { get; set; }
}
