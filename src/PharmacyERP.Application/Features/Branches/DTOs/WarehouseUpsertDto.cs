namespace PharmacyERP.Application.Features.Branches.DTOs;

public class WarehouseUpsertDto
{
    public int? Id { get; set; }
    public int BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
