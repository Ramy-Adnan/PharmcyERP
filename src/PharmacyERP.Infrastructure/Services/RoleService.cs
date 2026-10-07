using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Security;
using PharmacyERP.Application.Features.Security.DTOs;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Services;

public class RoleService : IRoleService
{
    private readonly IApplicationDbContext _context;

    public RoleService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<RoleDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _context.Roles
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Include(r => r.Users)
            .OrderByDescending(r => r.IsSystemRole).ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            IsSystemRole = r.IsSystemRole,
            UserCount = r.Users.Count,
            PermissionCodes = r.RolePermissions.Select(rp => rp.Permission.Code).ToList()
        }).ToList();
    }

    public async Task<RoleUpsertDto?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var role = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (role is null) return null;

        return new RoleUpsertDto
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            PermissionIds = role.RolePermissions.Select(rp => rp.PermissionId).ToList()
        };
    }

    public async Task<List<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await _context.Permissions
            .OrderBy(p => p.Module).ThenBy(p => p.DisplayName)
            .ToListAsync(cancellationToken);

        return permissions.Select(p => new PermissionDto
        {
            Id = p.Id,
            Code = p.Code,
            Module = p.Module,
            DisplayName = p.DisplayName,
            Description = p.Description
        }).ToList();
    }

    public async Task<Result<RoleDto>> CreateAsync(RoleUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(dto, cancellationToken);
        if (validation is not null) return Result<RoleDto>.Failure(validation);

        var role = new Role
        {
            Name = dto.Name.Trim(),
            Description = dto.Description,
            IsSystemRole = false
        };

        _context.Roles.Add(role);
        await _context.SaveChangesAsync(cancellationToken);

        await SyncPermissionsAsync(role.Id, dto.PermissionIds, cancellationToken);

        return Result<RoleDto>.Success((await GetAllAsync(cancellationToken)).First(r => r.Id == role.Id));
    }

    public async Task<Result<RoleDto>> UpdateAsync(RoleUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<RoleDto>.Failure("معرّف الدور مطلوب.");

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == dto.Id, cancellationToken);
        if (role is null) return Result<RoleDto>.Failure("الدور غير موجود.");

        if (role.IsSystemRole)
            return Result<RoleDto>.Failure("لا يمكن تعديل دور نظام أساسي (System Administrator).");

        var validation = await ValidateAsync(dto, cancellationToken);
        if (validation is not null) return Result<RoleDto>.Failure(validation);

        role.Name = dto.Name.Trim();
        role.Description = dto.Description;

        await _context.SaveChangesAsync(cancellationToken);
        await SyncPermissionsAsync(role.Id, dto.PermissionIds, cancellationToken);

        return Result<RoleDto>.Success((await GetAllAsync(cancellationToken)).First(r => r.Id == role.Id));
    }

    public async Task<Result> DeleteAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var role = await _context.Roles.Include(r => r.Users).FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null) return Result.Failure("الدور غير موجود.");

        if (role.IsSystemRole) return Result.Failure("لا يمكن حذف دور نظام أساسي.");
        if (role.Users.Any()) return Result.Failure("لا يمكن حذف دور مرتبط بمستخدمين. أعد تعيين المستخدمين إلى دور آخر أولاً.");

        _context.Roles.Remove(role);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task SyncPermissionsAsync(int roleId, List<int> permissionIds, CancellationToken cancellationToken)
    {
        var existing = await _context.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(cancellationToken);

        var toRemove = existing.Where(rp => !permissionIds.Contains(rp.PermissionId)).ToList();
        foreach (var rp in toRemove) _context.RolePermissions.Remove(rp);

        var existingPermissionIds = existing.Select(rp => rp.PermissionId).ToHashSet();
        foreach (var permissionId in permissionIds.Where(id => !existingPermissionIds.Contains(id)))
        {
            _context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> ValidateAsync(RoleUpsertDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "اسم الدور مطلوب.";

        var nameTaken = await _context.Roles.AnyAsync(r => r.Name == dto.Name.Trim() && r.Id != dto.Id, cancellationToken);
        if (nameTaken) return "اسم الدور مستخدم مسبقاً.";

        return null;
    }
}
