using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Insurance.DTOs;

namespace PharmacyERP.Application.Features.Insurance;

/// <summary>
/// Manages insurance companies, the policies linking customers to them, and
/// the claims lifecycle for reimbursement of covered sales. Claims are
/// created against an already-completed SalesInvoice — insurance billing in
/// this system is a back-office reimbursement process, not a point-of-sale
/// payment method (the customer still pays the full or co-pay amount at POS).
/// </summary>
public interface IInsuranceService
{
    // Insurance companies
    Task<List<InsuranceCompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default);
    Task<InsuranceCompanyUpsertDto?> GetCompanyForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<InsuranceCompanyDto>> CreateCompanyAsync(InsuranceCompanyUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<InsuranceCompanyDto>> UpdateCompanyAsync(InsuranceCompanyUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SetCompanyActiveStatusAsync(int companyId, bool isActive, CancellationToken cancellationToken = default);

    // Policies
    Task<List<InsurancePolicyDto>> GetPoliciesAsync(CancellationToken cancellationToken = default);
    Task<InsurancePolicyUpsertDto?> GetPolicyForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<InsurancePolicyDto>> CreatePolicyAsync(InsurancePolicyUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<InsurancePolicyDto>> UpdatePolicyAsync(InsurancePolicyUpsertDto dto, CancellationToken cancellationToken = default);
    Task<List<InsurancePolicyDto>> GetActivePoliciesForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    // Claims
    Task<List<InsuranceClaimDto>> GetClaimsAsync(CancellationToken cancellationToken = default);
    Task<Result<InsuranceClaimDto>> SubmitClaimAsync(SubmitClaimDto dto, CancellationToken cancellationToken = default);
    Task<Result<InsuranceClaimDto>> ProcessClaimAsync(ProcessClaimDto dto, CancellationToken cancellationToken = default);
}
