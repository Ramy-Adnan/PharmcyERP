using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Sales.DTOs;

public class CustomerCreditInvoiceDto
{
    public int InvoiceId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime SaleAtUtc { get; set; }
    public decimal InvoiceAmount { get; set; }
    public decimal ReturnedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount => Math.Max(0, InvoiceAmount - ReturnedAmount - PaidAmount);
    public string PaymentStatus => OutstandingAmount == 0 ? "مسدد" : PaidAmount > 0 ? "مسدد جزئياً" : "غير مسدد";
}

public class CustomerPaymentDto
{
    public string Number { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
}

public class CustomerAccountDto
{
    public string CustomerName { get; set; } = string.Empty;
    public List<CustomerCreditInvoiceDto> Invoices { get; set; } = new();
    public List<CustomerPaymentDto> Payments { get; set; } = new();
    public decimal OutstandingAmount => Invoices.Sum(i => i.OutstandingAmount);
}

public class CustomerDebtPaymentDto
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int CustomerId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? Notes { get; set; }
}
