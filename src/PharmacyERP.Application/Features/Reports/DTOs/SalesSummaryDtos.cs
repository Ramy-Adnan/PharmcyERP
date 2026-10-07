using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Reports.DTOs;

public class PaymentMethodAmountDto
{
    public PaymentMethod PaymentMethod { get; set; }
    public int InvoiceCount { get; set; }
    public decimal Amount { get; set; }
}

public class SalesSummaryDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    public int TotalInvoices { get; set; }
    public decimal GrossSales { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal NetSales { get; set; }

    public List<PaymentMethodAmountDto> ByPaymentMethod { get; set; } = new();
}
