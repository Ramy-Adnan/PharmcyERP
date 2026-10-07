using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

/// <summary>
/// Covers the license limits actually being enforced in the services rather
/// than merely displayed on the license screen. A licensing feature that the
/// business logic never consults is decoration, so these tests drive
/// BranchService with a permissive and an exhausted license and assert the
/// difference in behaviour.
/// </summary>
public class LicenseEnforcementTests
{
    private readonly FakeDateTime _clock = new();

    private static BranchUpsertDto NewBranch(string code) => new()
    {
        Code = code,
        Name = "فرع جديد",
        Type = BranchType.SubBranch,
        IsActive = true
    };

    [Fact]
    public async Task CreateBranch_Succeeds_WhenTheLicenseAllowsMoreBranches()
    {
        await using var context = TestDb.CreateContext(_clock);
        await TestDb.SeedBaselineAsync(context);
        var service = new BranchService(context, new PermissiveLicenseService());

        var result = await service.CreateAsync(NewBranch("BR2"));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task CreateBranch_IsRefused_WhenTheLicenseLimitIsReached()
    {
        await using var context = TestDb.CreateContext(_clock);
        await TestDb.SeedBaselineAsync(context);
        var service = new BranchService(context, new ExhaustedLicenseService());

        var result = await service.CreateAsync(NewBranch("BR2"));

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();

        // Only the baseline branch should exist — the refusal must happen before any insert.
        (await context.Branches.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateBranch_AlsoCreatesADefaultWarehouse_SoTheBranchIsImmediatelyUsable()
    {
        await using var context = TestDb.CreateContext(_clock);
        await TestDb.SeedBaselineAsync(context);
        var service = new BranchService(context, new PermissiveLicenseService());

        var result = await service.CreateAsync(NewBranch("BR2"));

        result.Succeeded.Should().BeTrue();

        var warehouses = await context.Warehouses.AsNoTracking()
            .Where(w => w.BranchId == result.Value!.Id).ToListAsync();

        warehouses.Should().HaveCount(1, "Inventory and Sales both require a warehouse to operate against");
        warehouses[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteBranch_IsRefused_ForTheMainBranch()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var service = new BranchService(context, new PermissiveLicenseService());

        var result = await service.DeleteAsync(fixture.BranchId);

        result.Succeeded.Should().BeFalse();
        (await context.Branches.AsNoTracking().CountAsync()).Should().Be(1);
    }
}
