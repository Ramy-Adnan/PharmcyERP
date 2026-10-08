using PharmacyERP.Domain.Common;
namespace PharmacyERP.Domain.Entities;
/// <summary>A named multiple of the item's immutable base stock unit.</summary>
public class ItemSaleUnit : BaseEntity
{
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int BaseUnitCount { get; set; }
    public string? Barcode { get; set; }
    public bool IsActive { get; set; } = true;
}
