using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A claim submitted to an InsuranceCompany for reimbursement of the covered
/// portion of a specific SalesInvoice, tracked through its own lifecycle
/// (Submitted → UnderReview → Approved/Rejected → Paid) independent of the
/// sale itself, which is already complete and paid by the customer at POS.
/// </summary>
public class InsuranceClaim : AuditableEntity
{
    public string ClaimNumber { get; set; } = string.Empty;

    public int SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    public int InsurancePolicyId { get; set; }
    public InsurancePolicy InsurancePolicy { get; set; } = null!;

    public decimal ClaimedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }

    public InsuranceClaimStatus Status { get; set; } = InsuranceClaimStatus.Submitted;

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Notes { get; set; }
}
