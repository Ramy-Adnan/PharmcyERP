using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.Application.Features.Security;
using PharmacyERP.Application.Features.Security.DTOs;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILicenseService _licenseService;

    public UserService(IApplicationDbContext context, IPasswordHasher passwordHasher, ILicenseService licenseService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _licenseService = licenseService;
    }

    public async Task<List<UserDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.DefaultBranch)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        return users.Select(u => new UserDto
        {
            Id = u.Id,
            FullName = u.FullName,
            Username = u.Username,
            Email = u.Email,
            PhoneNumber = u.PhoneNumber,
            Status = u.IsLockedOut() ? UserStatus.Locked : u.Status,
            RoleName = u.Role.Name,
            DefaultBranchName = u.DefaultBranch?.Name,
            LastLoginAtUtc = u.LastLoginAtUtc
        }).ToList();
    }

    public async Task<UserUpsertDto?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.UserBranches)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null) return null;

        return new UserUpsertDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Username = user.Username,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            RoleId = user.RoleId,
            DefaultBranchId = user.DefaultBranchId ?? 0,
            AdditionalBranchIds = user.UserBranches.Select(ub => ub.BranchId).ToList()
        };
    }

    public async Task<Result<UserDto>> CreateAsync(UserUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(dto, isNew: true, cancellationToken);
        if (validation is not null) return Result<UserDto>.Failure(validation);

        var activeUserCount = await _context.Users.CountAsync(u => u.Status == UserStatus.Active, cancellationToken);
        //if (!await _licenseService.CanAddUserAsync(activeUserCount, cancellationToken))
        //    return Result<UserDto>.Failure("تم الوصول إلى الحد الأقصى لعدد المستخدمين المسموح به بموجب الترخيص الحالي. الرجاء التواصل مع الجهة المرخِّصة لترقية الترخيص.");

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Username = dto.Username.Trim(),
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            RoleId = dto.RoleId,
            DefaultBranchId = dto.DefaultBranchId,
            Status = UserStatus.Active,
            PasswordHash = _passwordHasher.Hash(dto.Password!)
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        await SyncAdditionalBranchesAsync(user.Id, dto.AdditionalBranchIds, cancellationToken);

        return Result<UserDto>.Success((await GetAllAsync(cancellationToken)).First(u => u.Id == user.Id));
    }

    public async Task<Result<UserDto>> UpdateAsync(UserUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<UserDto>.Failure("معرّف المستخدم مطلوب.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.Id, cancellationToken);
        if (user is null) return Result<UserDto>.Failure("المستخدم غير موجود.");

        var validation = await ValidateAsync(dto, isNew: false, cancellationToken);
        if (validation is not null) return Result<UserDto>.Failure(validation);

        user.FullName = dto.FullName.Trim();
        user.Username = dto.Username.Trim();
        user.Email = dto.Email;
        user.PhoneNumber = dto.PhoneNumber;
        user.RoleId = dto.RoleId;
        user.DefaultBranchId = dto.DefaultBranchId;

        if (!string.IsNullOrWhiteSpace(dto.Password))
            user.PasswordHash = _passwordHasher.Hash(dto.Password);

        await _context.SaveChangesAsync(cancellationToken);
        await SyncAdditionalBranchesAsync(user.Id, dto.AdditionalBranchIds, cancellationToken);

        return Result<UserDto>.Success((await GetAllAsync(cancellationToken)).First(u => u.Id == user.Id));
    }

    public async Task<Result> SetStatusAsync(int userId, bool activate, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return Result.Failure("المستخدم غير موجود.");

        if (!activate)
        {
            var isLastActiveAdmin = await IsLastActiveAdministratorAsync(user, cancellationToken);
            if (isLastActiveAdmin) return Result.Failure("لا يمكن تعطيل آخر مستخدم مدير نظام نشط.");
        }

        user.Status = activate ? UserStatus.Active : UserStatus.Inactive;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(int userId, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return Result.Failure("يجب أن تتكون كلمة المرور من 8 أحرف على الأقل.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return Result.Failure("المستخدم غير موجود.");

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> UnlockAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return Result.Failure("المستخدم غير موجود.");

        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        if (user.Status == UserStatus.Locked) user.Status = UserStatus.Active;

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task SyncAdditionalBranchesAsync(int userId, List<int> branchIds, CancellationToken cancellationToken)
    {
        var existing = await _context.UserBranches.Where(ub => ub.UserId == userId).ToListAsync(cancellationToken);

        var toRemove = existing.Where(ub => !branchIds.Contains(ub.BranchId)).ToList();
        foreach (var ub in toRemove) _context.UserBranches.Remove(ub);

        var existingBranchIds = existing.Select(ub => ub.BranchId).ToHashSet();
        foreach (var branchId in branchIds.Where(id => !existingBranchIds.Contains(id)))
        {
            _context.UserBranches.Add(new UserBranch { UserId = userId, BranchId = branchId });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsLastActiveAdministratorAsync(User user, CancellationToken cancellationToken)
    {
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);
        if (role is null || !role.IsSystemRole) return false;

        var activeAdminCount = await _context.Users
            .CountAsync(u => u.RoleId == role.Id && u.Status == UserStatus.Active && u.Id != user.Id, cancellationToken);

        return activeAdminCount == 0;
    }

    private async Task<string?> ValidateAsync(UserUpsertDto dto, bool isNew, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName)) return "الاسم الكامل مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.Username)) return "اسم المستخدم مطلوب.";
        if (dto.RoleId <= 0) return "الرجاء اختيار دور للمستخدم.";
        if (dto.DefaultBranchId <= 0) return "الرجاء اختيار الفرع الافتراضي.";

        if (isNew && string.IsNullOrWhiteSpace(dto.Password))
            return "كلمة المرور مطلوبة عند إنشاء مستخدم جديد.";
        if (isNew && dto.Password!.Length < 8)
            return "يجب أن تتكون كلمة المرور من 8 أحرف على الأقل.";
        if (!isNew && !string.IsNullOrWhiteSpace(dto.Password) && dto.Password.Length < 8)
            return "يجب أن تتكون كلمة المرور من 8 أحرف على الأقل.";

        var usernameTaken = await _context.Users
            .AnyAsync(u => u.Username == dto.Username.Trim() && u.Id != dto.Id, cancellationToken);
        if (usernameTaken) return "اسم المستخدم مستخدم مسبقاً.";

        var roleExists = await _context.Roles.AnyAsync(r => r.Id == dto.RoleId, cancellationToken);
        if (!roleExists) return "الدور المحدد غير موجود.";

        var branchExists = await _context.Branches.AnyAsync(b => b.Id == dto.DefaultBranchId, cancellationToken);
        if (!branchExists) return "الفرع المحدد غير موجود.";

        return null;
    }
}
