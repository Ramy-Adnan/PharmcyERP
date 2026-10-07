using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// Grants a User access to a Branch beyond their default one — needed for
/// area managers, accountants, or roaming pharmacists who work across
/// multiple locations.
/// </summary>
public class UserBranch : BaseEntity
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
}
