namespace PharmacyERP.Application.Features.Inventory.DTOs;
public class ItemSaleUnitDto
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BaseUnitCount { get; set; } = 1;
    public string? Barcode { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal SalePrice { get; set; }
}
