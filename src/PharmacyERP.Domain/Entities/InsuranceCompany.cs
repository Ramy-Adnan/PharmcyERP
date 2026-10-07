using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>A third-party payer the pharmacy has a billing relationship with.</summary>
public class InsuranceCompany : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<InsurancePolicy> Policies { get; set; } = new List<InsurancePolicy>();
}
