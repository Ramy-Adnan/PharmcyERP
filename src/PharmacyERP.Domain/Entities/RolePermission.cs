using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>Join entity mapping a Role to the Permissions it grants.</summary>
public class RolePermission : BaseEntity
{
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public int PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
