using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Insurance.DTOs;

public class InsuranceClaimDto
{
    public int Id { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public string SalesInvoiceNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string InsuranceCompanyName { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public decimal ClaimedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public InsuranceClaimStatus Status { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Notes { get; set; }
}

public class SubmitClaimDto
{
    public int SalesInvoiceId { get; set; }
    public int InsurancePolicyId { get; set; }
    public decimal ClaimedAmount { get; set; }
    public string? Notes { get; set; }
}

public class ProcessClaimDto
{
    public int ClaimId { get; set; }
    public InsuranceClaimStatus NewStatus { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string? Notes { get; set; }
}
