using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Sales.DTOs;

public class SalesInvoiceDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? PrescriptionNumber { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public DateTime SaleAtUtc { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal InitialPaymentAmount { get; set; }
    public decimal DebtAtSale => PaymentMethod == PaymentMethod.Credit ? Math.Max(0, TotalAmount - InitialPaymentAmount) : 0;
    public decimal AmountTendered { get; set; }
    public decimal ChangeGiven { get; set; }
    public SalesInvoiceStatus Status { get; set; }
    public int LineCount { get; set; }
}

public class SalesInvoiceLineDto
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int UnitsPerSale { get; set; } = 1;
    public string UnitName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int QuantityReturned { get; set; }
    public int QuantityReturnable { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRatePercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class SalesInvoiceDetailDto
{
    public SalesInvoiceDto Header { get; set; } = null!;
    public List<SalesInvoiceLineDto> Lines { get; set; } = new();
}
