using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>A unit an item is stocked and sold in (علبة، شريط، قطعة، زجاجة...).</summary>
public class UnitOfMeasure : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Item> Items { get; set; } = new List<Item>();
}
