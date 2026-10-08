using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Purchasing.DTOs;

public class PurchaseOrderDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public string? Notes { get; set; }
    public decimal TotalAmount { get; set; }
    public int LineCount { get; set; }
    public bool HasOutstandingLines { get; set; }
}

public class PurchaseOrderLineDto
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UnitOfMeasureName { get; set; } = string.Empty;
    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public int QuantityOutstanding { get; set; }
    public decimal UnitCost { get; set; }
    public decimal SalePrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal LineTotal { get; set; }
}

public class PurchaseOrderUpsertDto
{
    public int? Id { get; set; }
    public int SupplierId { get; set; }
    public int BranchId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.Today;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseOrderLineUpsertDto> Lines { get; set; } = new();
}

public class PurchaseOrderLineUpsertDto
{
    public int? Id { get; set; }
    public int ItemId { get; set; }
    public int QuantityOrdered { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal TaxRatePercent { get; set; }
}
