using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Reports.DTOs;

public class PaymentMethodAmountDto
{
    public PaymentMethod PaymentMethod { get; set; }
    public string PaymentMethodName => PaymentMethod switch { PaymentMethod.Cash => "نقدي", PaymentMethod.Card => "بطاقة", PaymentMethod.Credit => "آجل", _ => "مختلط" };
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

    public decimal PaidSales => ByPaymentMethod.Where(p => p.PaymentMethod is PaymentMethod.Cash or PaymentMethod.Card).Sum(p => p.Amount);
    public decimal CreditSales => ByPaymentMethod.Where(p => p.PaymentMethod == PaymentMethod.Credit).Sum(p => p.Amount);

    public List<PaymentMethodAmountDto> ByPaymentMethod { get; set; } = new();
}
