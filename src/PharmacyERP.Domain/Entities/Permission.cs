using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A single granular capability in the system, e.g. "Sales.CreateInvoice",
/// "Inventory.AdjustStock", "Accounting.PostJournalEntry". Permission codes
/// are checked by the WPF Shell to show/hide menu items and by the
/// Application layer to authorize use cases.
/// </summary>
public class Permission : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
