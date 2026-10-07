using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>The pharmaceutical manufacturer/company that produces an item.</summary>
public class Manufacturer : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Country { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Item> Items { get; set; } = new List<Item>();
}
