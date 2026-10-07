using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Auth;
using PharmacyERP.Application.Features.Auth.DTOs;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

/// <summary>
/// Full login workflow implementation: validates credentials, enforces the
/// account-lockout policy (5 failed attempts -> 15 minute lockout), resolves
/// which branch the user should land on, aggregates their effective
/// permissions from their Role, and writes every attempt to LoginHistory.
/// </summary>
public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTime _dateTime;

    public AuthService(IApplicationDbContext context, IPasswordHasher passwordHasher, IDateTime dateTime)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _dateTime = dateTime;
    }

    public async Task<Result<LoginResultDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Include(u => u.DefaultBranch)
            .Include(u => u.UserBranches).ThenInclude(ub => ub.Branch)
            .FirstOrDefaultAsync(u => u.Username == request.Username, cancellationToken);

        if (user is null)
        {
            await LogAttemptAsync(null, request, succeeded: false, "اسم المستخدم غير موجود", cancellationToken);
            return Result<LoginResultDto>.Failure("اسم المستخدم أو كلمة المرور غير صحيحة.");
        }

        if (user.IsLockedOut())
        {
            await LogAttemptAsync(user.Id, request, succeeded: false, "الحساب مقفل مؤقتاً", cancellationToken);
            var remaining = user.LockoutEndUtc.HasValue ? user.LockoutEndUtc.Value - _dateTime.UtcNow : TimeSpan.Zero;
            return Result<LoginResultDto>.Failure(
                $"الحساب مقفل مؤقتاً بسبب محاولات دخول فاشلة متكررة. حاول مجدداً بعد {Math.Max(1, (int)remaining.TotalMinutes)} دقيقة.");
        }

        if (user.Status != UserStatus.Active)
        {
            await LogAttemptAsync(user.Id, request, succeeded: false, "الحساب غير مفعّل", cancellationToken);
            return Result<LoginResultDto>.Failure("هذا الحساب غير مفعّل. الرجاء مراجعة مدير النظام.");
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEndUtc = _dateTime.UtcNow.Add(LockoutDuration);
            }
            await _context.SaveChangesAsync(cancellationToken);

            await LogAttemptAsync(user.Id, request, succeeded: false, "كلمة مرور خاطئة", cancellationToken);
            return Result<LoginResultDto>.Failure("اسم المستخدم أو كلمة المرور غير صحيحة.");
        }

        // Successful login: reset lockout counters.
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAtUtc = _dateTime.UtcNow;

        var resolvedBranch = ResolveBranch(user, request.RequestedBranchId);
        if (resolvedBranch is null)
        {
            return Result<LoginResultDto>.Failure("لا يوجد فرع مرتبط بهذا المستخدم. الرجاء مراجعة مدير النظام.");
        }

        await _context.SaveChangesAsync(cancellationToken);
        await LogAttemptAsync(user.Id, request, succeeded: true, null, cancellationToken);

        var permissions = user.Role.RolePermissions
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var availableBranches = new List<BranchSummaryDto>();
        if (user.DefaultBranch is not null)
            availableBranches.Add(new BranchSummaryDto { Id = user.DefaultBranch.Id, Code = user.DefaultBranch.Code, Name = user.DefaultBranch.Name });
        availableBranches.AddRange(user.UserBranches
            .Where(ub => ub.Branch.Id != user.DefaultBranchId)
            .Select(ub => new BranchSummaryDto { Id = ub.Branch.Id, Code = ub.Branch.Code, Name = ub.Branch.Name }));

        var result = new LoginResultDto
        {
            UserId = user.Id,
            FullName = user.FullName,
            Username = user.Username,
            RoleName = user.Role.Name,
            BranchId = resolvedBranch.Id,
            BranchName = resolvedBranch.Name,
            Permissions = permissions,
            AvailableBranches = availableBranches
        };

        return Result<LoginResultDto>.Success(result);
    }

    public async Task LogoutAsync(int userId, CancellationToken cancellationToken = default)
    {
        // Reserved for future session-tracking (e.g. closing an open cash-drawer session).
        await Task.CompletedTask;
    }

    public async Task<Result> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Result.Failure("المستخدم غير موجود.");

        if (!_passwordHasher.Verify(currentPassword, user.PasswordHash))
            return Result.Failure("كلمة المرور الحالية غير صحيحة.");

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return Result.Failure("يجب أن تتكون كلمة المرور الجديدة من 8 أحرف على الأقل.");

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        user.Status = UserStatus.Active;
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Branch? ResolveBranch(User user, int? requestedBranchId)
    {
        if (requestedBranchId.HasValue)
        {
            if (user.DefaultBranchId == requestedBranchId) return user.DefaultBranch;
            var match = user.UserBranches.FirstOrDefault(ub => ub.BranchId == requestedBranchId.Value);
            if (match is not null) return match.Branch;
        }

        return user.DefaultBranch ?? user.UserBranches.FirstOrDefault()?.Branch;
    }

    private async Task LogAttemptAsync(int? userId, LoginRequestDto request, bool succeeded, string? failureReason, CancellationToken cancellationToken)
    {
        _context.LoginHistories.Add(new LoginHistory
        {
            UserId = userId,
            UsernameAttempted = request.Username,
            WasSuccessful = succeeded,
            FailureReason = failureReason,
            AttemptedAtUtc = _dateTime.UtcNow,
            MachineName = request.MachineName,
            IpAddress = request.IpAddress
        });
        await _context.SaveChangesAsync(cancellationToken);
    }
}
