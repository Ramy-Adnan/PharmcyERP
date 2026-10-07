using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Security.DTOs;

namespace PharmacyERP.Application.Features.Security;

/// <summary>Full CRUD for Roles and their assigned Permissions.</summary>
public interface IRoleService
{
    Task<List<RoleDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RoleUpsertDto?> GetForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<List<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default);
    Task<Result<RoleDto>> CreateAsync(RoleUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<RoleDto>> UpdateAsync(RoleUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(int roleId, CancellationToken cancellationToken = default);
}
