namespace PharmacyERP.Application.Features.Security.DTOs;

public class RoleUpsertDto
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<int> PermissionIds { get; set; } = new();
}
