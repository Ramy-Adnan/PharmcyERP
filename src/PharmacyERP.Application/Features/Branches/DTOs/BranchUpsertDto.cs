using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Branches.DTOs;

public class BranchUpsertDto
{
    public int? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BranchType Type { get; set; } = BranchType.SubBranch;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string? LicenseNumber { get; set; }
    public bool IsActive { get; set; } = true;
}
