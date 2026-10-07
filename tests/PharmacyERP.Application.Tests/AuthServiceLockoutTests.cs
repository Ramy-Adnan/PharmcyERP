using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Auth.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

/// <summary>
/// Covers the account-lockout policy in AuthService: five consecutive failures
/// lock the account for fifteen minutes. This is the only brute-force control
/// on the login screen, so it is worth asserting the exact threshold rather
/// than just "eventually locks".
/// </summary>
public class AuthServiceLockoutTests
{
    private const string CorrectPassword = "Test-only-password";

    private readonly FakeDateTime _clock = new();
    private readonly FakePasswordHasher _hasher = new();

    private async Task<int> SeedUserAsync(Microsoft.EntityFrameworkCore.DbContext context, int branchId)
    {
        var role = new Role { Name = "System Administrator", IsSystemRole = true };
        context.Add(role);
        await context.SaveChangesAsync();

        var user = new User
        {
            FullName = "مدير النظام",
            Username = "admin",
            PasswordHash = _hasher.Hash(CorrectPassword),
            Status = UserStatus.Active,
            RoleId = role.Id,
            DefaultBranchId = branchId
        };
        context.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private static LoginRequestDto Attempt(string password) =>
        new() { Username = "admin", Password = password, MachineName = "TEST" };

    [Fact]
    public async Task Login_Succeeds_WithCorrectCredentials()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        await SeedUserAsync(context, fixture.BranchId);
        var service = new AuthService(context, _hasher, _clock);

        var result = await service.LoginAsync(Attempt(CorrectPassword));

        result.Succeeded.Should().BeTrue();
        result.Value!.Username.Should().Be("admin");
        result.Value.BranchId.Should().Be(fixture.BranchId);
    }

    [Fact]
    public async Task Login_DoesNotLockTheAccount_AfterFourFailedAttempts()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var userId = await SeedUserAsync(context, fixture.BranchId);
        var service = new AuthService(context, _hasher, _clock);

        for (var i = 0; i < 4; i++)
            await service.LoginAsync(Attempt("wrong-password"));

        var user = await context.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        user.FailedLoginAttempts.Should().Be(4);
        user.LockoutEndUtc.Should().BeNull("the lockout threshold is five, so four failures must not lock the account");

        // Proving the account is still usable matters as much as the counter value.
        (await service.LoginAsync(Attempt(CorrectPassword))).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Login_LocksTheAccount_OnTheFifthFailedAttempt()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var userId = await SeedUserAsync(context, fixture.BranchId);
        var service = new AuthService(context, _hasher, _clock);

        for (var i = 0; i < 5; i++)
            await service.LoginAsync(Attempt("wrong-password"));

        var user = await context.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        user.FailedLoginAttempts.Should().Be(5);
        user.LockoutEndUtc.Should().NotBeNull();

        // Even the *correct* password must be refused while the lockout window is open,
        // otherwise the lockout provides no protection at all.
        (await service.LoginAsync(Attempt(CorrectPassword))).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Login_SucceedsAgain_OnceTheLockoutWindowHasElapsed()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        await SeedUserAsync(context, fixture.BranchId);
        var service = new AuthService(context, _hasher, _clock);

        for (var i = 0; i < 5; i++)
            await service.LoginAsync(Attempt("wrong-password"));

        _clock.Advance(TimeSpan.FromMinutes(16));

        var result = await service.LoginAsync(Attempt(CorrectPassword));

        result.Succeeded.Should().BeTrue("the lockout is temporary (15 minutes), not permanent");
    }

    [Fact]
    public async Task Login_ResetsTheFailureCounter_AfterASuccessfulLogin()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var userId = await SeedUserAsync(context, fixture.BranchId);
        var service = new AuthService(context, _hasher, _clock);

        await service.LoginAsync(Attempt("wrong-password"));
        await service.LoginAsync(Attempt("wrong-password"));
        await service.LoginAsync(Attempt(CorrectPassword));

        var user = await context.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        user.FailedLoginAttempts.Should().Be(0, "a stale counter would lock a legitimate user out later for no reason");
    }

    [Fact]
    public async Task Login_RecordsEveryAttempt_SuccessfulOrNot()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        await SeedUserAsync(context, fixture.BranchId);
        var service = new AuthService(context, _hasher, _clock);

        await service.LoginAsync(Attempt("wrong-password"));
        await service.LoginAsync(Attempt(CorrectPassword));

        var history = await context.LoginHistories.AsNoTracking().ToListAsync();
        history.Should().HaveCount(2);
        history.Count(h => h.WasSuccessful).Should().Be(1);
        history.Count(h => !h.WasSuccessful).Should().Be(1);
    }
}
