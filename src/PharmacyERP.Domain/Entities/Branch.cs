using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A physical pharmacy location. Every transactional entity in the system
/// (invoices, stock, cash sessions, etc.) is scoped to a Branch so the chain
/// can be operated and reported on per-location or consolidated.
/// </summary>
public class Branch : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BranchType Type { get; set; } = BranchType.SubBranch;

    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string? LicenseNumber { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsMainBranch { get; set; }

    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
    public ICollection<UserBranch> UserBranches { get; set; } = new List<UserBranch>();
}
