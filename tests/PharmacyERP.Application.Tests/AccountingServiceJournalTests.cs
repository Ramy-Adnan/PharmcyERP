using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

/// <summary>
/// Covers the double-entry invariant in CreateManualEntryAsync. An accounting
/// module that lets an unbalanced entry through is worse than no accounting
/// module at all, because every downstream report silently inherits the error.
/// </summary>
public class AccountingServiceJournalTests
{
    private readonly FakeDateTime _clock = new();

    private static async Task<(int cashAccountId, int revenueAccountId)> SeedAccountsAsync(
        Microsoft.EntityFrameworkCore.DbContext context)
    {
        var cash = new ChartOfAccount { Code = "1110", Name = "النقدية", Type = AccountType.Asset, IsSystemAccount = true, IsActive = true };
        var revenue = new ChartOfAccount { Code = "4100", Name = "إيرادات المبيعات", Type = AccountType.Revenue, IsSystemAccount = true, IsActive = true };
        context.AddRange(cash, revenue);
        await context.SaveChangesAsync();
        return (cash.Id, revenue.Id);
    }

    [Fact]
    public async Task CreateManualEntry_Succeeds_WhenDebitsEqualCredits()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var (cashId, revenueId) = await SeedAccountsAsync(context);
        var service = new AccountingService(context, _clock);

        var result = await service.CreateManualEntryAsync(new ManualJournalEntryUpsertDto
        {
            BranchId = fixture.BranchId,
            EntryDate = _clock.UtcNow.Date,
            Description = "قيد تجريبي متوازن",
            Lines = new List<JournalEntryLineInputDto>
            {
                new() { AccountId = cashId, DebitAmount = 500m, CreditAmount = 0m },
                new() { AccountId = revenueId, DebitAmount = 0m, CreditAmount = 500m }
            }
        });

        result.Succeeded.Should().BeTrue();

        var savedLines = await context.JournalEntryLines.AsNoTracking().ToListAsync();
        savedLines.Should().HaveCount(2);
        savedLines.Sum(l => l.DebitAmount).Should().Be(savedLines.Sum(l => l.CreditAmount));
    }

    [Fact]
    public async Task CreateManualEntry_Fails_AndSavesNothing_WhenEntryIsUnbalanced()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var (cashId, revenueId) = await SeedAccountsAsync(context);
        var service = new AccountingService(context, _clock);

        var result = await service.CreateManualEntryAsync(new ManualJournalEntryUpsertDto
        {
            BranchId = fixture.BranchId,
            Description = "قيد غير متوازن",
            Lines = new List<JournalEntryLineInputDto>
            {
                new() { AccountId = cashId, DebitAmount = 500m, CreditAmount = 0m },
                new() { AccountId = revenueId, DebitAmount = 0m, CreditAmount = 400m }
            }
        });

        result.Succeeded.Should().BeFalse();
        (await context.JournalEntries.AsNoTracking().CountAsync()).Should().Be(0);
        (await context.JournalEntryLines.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateManualEntry_Fails_WhenASingleLineHasBothDebitAndCredit()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var (cashId, revenueId) = await SeedAccountsAsync(context);
        var service = new AccountingService(context, _clock);

        // Totals balance here (500/500), so only the per-line rule can catch this.
        var result = await service.CreateManualEntryAsync(new ManualJournalEntryUpsertDto
        {
            BranchId = fixture.BranchId,
            Description = "سطر يحمل مدين ودائن",
            Lines = new List<JournalEntryLineInputDto>
            {
                new() { AccountId = cashId, DebitAmount = 500m, CreditAmount = 500m },
                new() { AccountId = revenueId, DebitAmount = 0m, CreditAmount = 0m }
            }
        });

        result.Succeeded.Should().BeFalse();
        (await context.JournalEntries.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateManualEntry_Fails_WhenGivenFewerThanTwoLines()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var (cashId, _) = await SeedAccountsAsync(context);
        var service = new AccountingService(context, _clock);

        var result = await service.CreateManualEntryAsync(new ManualJournalEntryUpsertDto
        {
            BranchId = fixture.BranchId,
            Description = "قيد بسطر واحد",
            Lines = new List<JournalEntryLineInputDto>
            {
                new() { AccountId = cashId, DebitAmount = 100m, CreditAmount = 0m }
            }
        });

        result.Succeeded.Should().BeFalse();
    }
}
