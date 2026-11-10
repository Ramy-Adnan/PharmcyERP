using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.Application.Features.Auth.DTOs;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

/// <summary>
/// Exercises the production SaveChanges boundary with real EF tracking, including
/// authentication recovery and the long-lived-context failure path used by WPF.
/// </summary>
public sealed class LicenseWriteBoundaryTests
{
    private readonly FakeDateTime _clock = new();

    [Fact]
    public async Task NoLicense_IsReadOnlyByDefault_NotOnlyAfterLogin()
    {
        var license = new ControlledLicense();
        await using var db = Create(license, new LicenseRuntimeState());
        db.ItemCategories.Add(new() { Code = "NEW", Name = "دواء" });

        await Assert.ThrowsAsync<LicenseWriteDeniedException>(() => db.SaveChangesAsync());

        (await db.ItemCategories.AsNoTracking().CountAsync()).Should().Be(0);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ExplicitBootstrap_OnlyAllowsWritesWithinItsLifetime()
    {
        var runtime = new LicenseRuntimeState();
        await using var db = Create(new ControlledLicense(), runtime);
        runtime.IsBootstrapping.Should().BeFalse();
        using (runtime.BeginBootstrap())
        {
            runtime.IsBootstrapping.Should().BeTrue();
            db.ItemCategories.Add(new() { Code = "SEED", Name = "عام" });
            await db.SaveChangesAsync();
        }
        runtime.IsBootstrapping.Should().BeFalse();
        db.ItemCategories.Add(new() { Code = "SALE", Name = "إضافة بعد التهيئة" });

        await Assert.ThrowsAsync<LicenseWriteDeniedException>(() => db.SaveChangesAsync());
        (await db.ItemCategories.AsNoTracking().Select(category => category.Code).ToListAsync()).Should().Equal("SEED");
    }

    [Fact]
    public async Task DeniedStockAndInvoiceChanges_CannotLeakIntoAFollowingLoginSave()
    {
        var license = new ControlledLicense();
        var runtime = new LicenseRuntimeState();
        await using var db = Create(license, runtime);
        int batchId;
        using (runtime.BeginBootstrap())
        {
            var fixture = await TestDb.SeedBaselineAsync(db);
            batchId = await TestDb.AddBatchAsync(db, fixture, "STOCK", _clock.UtcNow.AddYears(1), 7);
        }
        var batch = await db.Batches.SingleAsync(batch => batch.Id == batchId);
        batch.QuantityOnHand = 2;
        db.SalesInvoices.Add(new() { Number = "SHOULD-NOT-EXIST", TotalAmount = 5000 });

        await Assert.ThrowsAsync<LicenseWriteDeniedException>(() => db.SaveChangesAsync());
        batch.QuantityOnHand.Should().Be(7, "denied edits are restored before detaching");
        db.LoginHistories.Add(new() { UsernameAttempted = "admin", WasSuccessful = true });
        await db.SaveChangesAsync();

        (await db.Batches.AsNoTracking().SingleAsync(batch => batch.Id == batchId)).QuantityOnHand.Should().Be(7);
        (await db.SalesInvoices.AsNoTracking().CountAsync()).Should().Be(0);
        (await db.LoginHistories.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ExpiredLicense_AllowsLoginFailedAttemptHistoryAndPasswordRecovery()
    {
        var runtime = new LicenseRuntimeState();
        await using var db = Create(new ControlledLicense(), runtime);
        int userId;
        using (runtime.BeginBootstrap())
        {
            var fixture = await TestDb.SeedBaselineAsync(db);
            var user = new User { Username = "admin", FullName = "مدير", PasswordHash = "hashed:original-pass", DefaultBranchId = fixture.BranchId,
                Role = new Role { Name = "Administrator" } };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }
        var auth = new AuthService(db, new FakePasswordHasher(), _clock);

        var failed = await auth.LoginAsync(new LoginRequestDto { Username = "admin", Password = "wrong" });
        var login = await auth.LoginAsync(new LoginRequestDto { Username = "admin", Password = "original-pass" });
        var password = await auth.ChangePasswordAsync(userId, "original-pass", "replacement-pass");

        failed.Succeeded.Should().BeFalse();
        login.Succeeded.Should().BeTrue();
        password.Succeeded.Should().BeTrue();
        (await db.LoginHistories.AsNoTracking().CountAsync()).Should().Be(2);
        (await db.Users.AsNoTracking().SingleAsync()).PasswordHash.Should().Be("hashed:replacement-pass");
    }

    [Theory]
    [InlineData("RoleId")]
    [InlineData("Status")]
    [InlineData("IsDeleted")]
    [InlineData("FullName")]
    [InlineData("DefaultBranchId")]
    public async Task AuthenticationException_CannotChangeRolesSeatsOrOtherProfileData(string property)
    {
        var runtime = new LicenseRuntimeState();
        await using var db = Create(new ControlledLicense(), runtime);
        var user = new User { Username = "user", FullName = "Original", PasswordHash = "hash", RoleId = 1, DefaultBranchId = 1 };
        using (runtime.BeginBootstrap())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        switch (property)
        {
            case "RoleId": user.RoleId = 2; break;
            case "Status": user.Status = UserStatus.Inactive; break;
            case "IsDeleted": user.IsDeleted = true; break;
            case "FullName": user.FullName = "Altered"; break;
            case "DefaultBranchId": user.DefaultBranchId = 2; break;
        }
        user.PasswordHash = "new-hash";

        await Assert.ThrowsAsync<LicenseWriteDeniedException>(() => db.SaveChangesAsync());
        var stored = await db.Users.AsNoTracking().SingleAsync();
        stored.PasswordHash.Should().Be("hash");
        stored.RoleId.Should().Be(1);
        stored.Status.Should().Be(UserStatus.Active);
        stored.FullName.Should().Be("Original");
    }

    [Fact]
    public async Task LoginHistory_ExceptionOnlyAllowsAppend_NotDeletingSecurityHistory()
    {
        await using var db = Create(new ControlledLicense(), new LicenseRuntimeState());
        var history = new LoginHistory { UsernameAttempted = "admin" };
        db.LoginHistories.Add(history);
        await db.SaveChangesAsync();
        db.LoginHistories.Remove(history);

        await Assert.ThrowsAsync<LicenseWriteDeniedException>(() => db.SaveChangesAsync());
        (await db.LoginHistories.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public void SynchronousSave_AlsoEnforcesTheWriteBoundary()
    {
        using var db = Create(new ControlledLicense(), new LicenseRuntimeState());
        db.ItemCategories.Add(new() { Code = "NEW", Name = "دواء" });

        Assert.Throws<LicenseWriteDeniedException>(() => db.SaveChanges());
        db.ItemCategories.AsNoTracking().Count().Should().Be(0);
    }

    [Fact]
    public async Task Renewal_ReenablesBusinessWritesWithoutReopeningTheContext()
    {
        var license = new ControlledLicense();
        await using var db = Create(license, new LicenseRuntimeState());
        db.ItemCategories.Add(new() { Code = "DENIED", Name = "غير مسموح" });
        await Assert.ThrowsAsync<LicenseWriteDeniedException>(() => db.SaveChangesAsync());
        license.Valid = true;
        db.ItemCategories.Add(new() { Code = "RENEWED", Name = "مسموح" });

        await db.SaveChangesAsync();

        (await db.ItemCategories.AsNoTracking().Select(category => category.Code).ToListAsync()).Should().Equal("RENEWED");
    }

    [Fact]
    public async Task EarlySalesAndAccountingDenial_ReturnsFailureBeforeAnyTrackedEdit()
    {
        var license = new ControlledLicense();
        await using var db = Create(license, new LicenseRuntimeState());
        var accounting = new AccountingService(db, _clock, license);
        var inventory = new InventoryService(db, _clock);
        var sales = new SalesService(db, inventory, accounting, _clock, new FakeCurrentUserService(), license);

        var checkout = await sales.CheckoutAsync(new SalesCheckoutDto(), 1);
        var expense = await accounting.CreateExpenseAsync(new ExpenseCreateDto());

        checkout.Succeeded.Should().BeFalse();
        expense.Succeeded.Should().BeFalse();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ReactivatingAnInactiveBranch_CannotBypassTheBranchLimit()
    {
        var license = new ControlledLicense { Valid = true, MaxBranches = 1 };
        await using var db = TestDb.CreateContext(_clock);
        await TestDb.SeedBaselineAsync(db);
        var inactive = new Branch { Code = "OTHER", Name = "ثانوي", IsActive = false };
        db.Branches.Add(inactive);
        await db.SaveChangesAsync();
        var service = new BranchService(db, license);

        var result = await service.SetActiveStatusAsync(inactive.Id, true);
        var update = await service.UpdateAsync(new BranchUpsertDto { Id = inactive.Id, Code = inactive.Code, Name = inactive.Name, IsActive = true });

        result.Succeeded.Should().BeFalse();
        update.Succeeded.Should().BeFalse();
        (await db.Branches.AsNoTracking().SingleAsync(branch => branch.Id == inactive.Id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task InactiveUserCannotBeReactivatedBeyondLicensedSeats_AndLockedSeatsStillCount()
    {
        var license = new ControlledLicense { Valid = true, MaxUsers = 1 };
        await using var db = TestDb.CreateContext(_clock);
        db.Users.Add(new() { Username = "locked", FullName = "محجوز", Status = UserStatus.Locked });
        var inactive = new User { Username = "inactive", FullName = "غير نشط", Status = UserStatus.Inactive };
        db.Users.Add(inactive);
        await db.SaveChangesAsync();
        var service = new UserService(db, new FakePasswordHasher(), license);

        var result = await service.SetStatusAsync(inactive.Id, true);

        result.Succeeded.Should().BeFalse();
        license.LastUserCount.Should().Be(1);
        (await db.Users.AsNoTracking().SingleAsync(user => user.Id == inactive.Id)).Status.Should().Be(UserStatus.Inactive);
    }

    private ApplicationDbContext Create(ILicenseService license, LicenseRuntimeState runtime)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new LicenseSaveChangesInterceptor(license, runtime)).Options;
        return new ApplicationDbContext(options, new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock));
    }

    private sealed class ControlledLicense : ILicenseService
    {
        public bool Valid { get; set; }
        public int MaxBranches { get; set; } = 5;
        public int MaxUsers { get; set; } = 5;
        public int LastUserCount { get; private set; }
        public string GetHardwareFingerprint() => "TEST";
        public Task<LicenseStatusDto> GetLicenseStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LicenseStatusDto { IsPresent = Valid, IsValid = Valid, IsExpired = !Valid, Message = Valid ? "صالح" : "انتهى الترخيص" });
        public Task<Result<LicenseInfoDto>> ActivateLicenseAsync(string content, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LicenseInfoDto>.Failure("Use the real signed license tests for activation."));
        public Task<bool> CanAddBranchAsync(int count, CancellationToken cancellationToken = default) => Task.FromResult(Valid && count < MaxBranches);
        public Task<bool> CanAddUserAsync(int count, CancellationToken cancellationToken = default)
        {
            LastUserCount = count;
            return Task.FromResult(Valid && count < MaxUsers);
        }
    }
}
