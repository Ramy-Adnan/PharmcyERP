namespace PharmacyERP.Application.Features.Inventory.DTOs;

public class ItemCategoryDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int ItemCount { get; set; }
}

public class UnitOfMeasureDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int ItemCount { get; set; }
}

public class ManufacturerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Country { get; set; }
    public bool IsActive { get; set; }
    public int ItemCount { get; set; }
}
