using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Accounting.DTOs;

/// <summary>
/// Posting requests carry only the amounts and identifiers the calling module
/// (Sales, Purchasing, Insurance) already has in hand — AccountingService
/// never re-queries another module's tables, keeping the modules decoupled.
/// </summary>
public class SalesInvoicePostingRequest
{
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public int BranchId { get; set; }
    public int SalesInvoiceId { get; set; }
    public string SalesInvoiceNumber { get; set; } = string.Empty;
    public DateTime SaleDate { get; set; }

    /// <summary>Net revenue = TotalAmount - TaxAmount (i.e. subtotal after discounts, before tax).</summary>
    public decimal NetRevenueAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class PurchaseInvoicePostingRequest
{
    public int BranchId { get; set; }
    public int PurchaseInvoiceId { get; set; }
    public string PurchaseInvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }

    /// <summary>Net inventory cost = TotalAmount - TaxAmount.</summary>
    public decimal NetInventoryAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class PurchaseInvoicePaymentPostingRequest
{
    public int BranchId { get; set; }
    public int PurchaseInvoiceId { get; set; }
    public string PurchaseInvoiceNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
}

public class InsuranceClaimPaymentPostingRequest
{
    public int BranchId { get; set; }
    public int InsuranceClaimId { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public string InsuranceCompanyName { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
}

public class PayrollPaymentPostingRequest
{
    public int BranchId { get; set; }
    public int PayrollRunId { get; set; }
    public string PayrollRunNumber { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
}
