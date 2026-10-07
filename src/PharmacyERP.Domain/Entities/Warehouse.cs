using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A storage location within a Branch. Most branches have a single
/// "sales floor" warehouse, but larger branches may also have a
/// back-store/quarantine warehouse for expired or recalled stock.
/// </summary>
public class Warehouse : AuditableEntity
{
    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
