using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A named collection of permissions (e.g. "Pharmacist", "Cashier", "Branch Manager",
/// "System Administrator"). Roles are defined per-tenant/installation so the
/// pharmacy owner can tailor access without code changes.
/// </summary>
public class Role : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>System roles (e.g. "Administrator") cannot be deleted or renamed.</summary>
    public bool IsSystemRole { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
