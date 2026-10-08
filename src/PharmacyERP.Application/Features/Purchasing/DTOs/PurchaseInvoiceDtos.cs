using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Purchasing.DTOs;

public class PurchaseInvoiceDto
{
    public int SupplierId { get; set; }
    public int BranchId { get; set; }
    public int? GoodsReceiptNoteId { get; set; }
    public PurchasePricingType PurchaseType { get; set; }
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? GoodsReceiptNumber { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public PurchaseInvoiceStatus Status { get; set; }
    public string? Notes { get; set; }
}

public class PurchaseInvoiceLineDto
{
    public int Id { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class PurchaseInvoiceUpsertDto
{
    public PurchasePricingType PurchaseType { get; set; }
    public int SupplierId { get; set; }
    public int? GoodsReceiptNoteId { get; set; }
    public int BranchId { get; set; }
    public DateTime InvoiceDate { get; set; } = DateTime.Today;
    public DateTime? DueDate { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? Notes { get; set; }
    public List<PurchaseInvoiceLineUpsertDto> Lines { get; set; } = new();
}

public class PurchaseInvoiceLineUpsertDto
{
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
}

public class RecordPaymentDto
{
    public int PurchaseInvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}
