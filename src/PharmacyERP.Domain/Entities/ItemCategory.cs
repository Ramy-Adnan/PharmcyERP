using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>Classifies items for reporting and browsing (e.g. "مسكنات", "مضادات حيوية", "مستحضرات تجميل").</summary>
public class ItemCategory : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Item> Items { get; set; } = new List<Item>();
}
