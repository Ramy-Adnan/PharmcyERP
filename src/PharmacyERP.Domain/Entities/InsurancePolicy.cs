using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// Links a Customer to an InsuranceCompany under a specific policy number,
/// with the coverage percentage the pharmacy will claim back for that
/// customer's purchases while the policy is active and within its date range.
/// </summary>
public class InsurancePolicy : AuditableEntity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int InsuranceCompanyId { get; set; }
    public InsuranceCompany InsuranceCompany { get; set; } = null!;

    public string PolicyNumber { get; set; } = string.Empty;
    public decimal CoveragePercent { get; set; }

    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<InsuranceClaim> Claims { get; set; } = new List<InsuranceClaim>();

    public bool IsCurrentlyValid(DateTime asOfDate) =>
        IsActive && StartDate.Date <= asOfDate.Date && (!EndDate.HasValue || EndDate.Value.Date >= asOfDate.Date);
}
