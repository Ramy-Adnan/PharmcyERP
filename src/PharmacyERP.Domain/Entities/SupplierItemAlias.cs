using PharmacyERP.Domain.Common;
namespace PharmacyERP.Domain.Entities;
public class SupplierItemAlias : BaseEntity
{
    public int SupplierId { get; set; }
    public int ItemId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
}
