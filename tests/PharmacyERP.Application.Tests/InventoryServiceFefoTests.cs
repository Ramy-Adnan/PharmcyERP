using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

/// <summary>
/// Covers IssueStockFefoAsync — the single point through which Sales touches
/// stock. Getting FEFO wrong in a pharmacy means dispensing medicine that
/// expires sooner than stock left sitting on the shelf, so these tests assert
/// the picking order explicitly rather than just checking the total came out right.
/// </summary>
public class InventoryServiceFefoTests
{
    private readonly FakeDateTime _clock = new();

    [Fact]
    public async Task IssueStockFefo_TakesFromTheEarliestExpiringBatchFirst()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var service = new InventoryService(context, _clock);

        // Deliberately inserted newest-expiry-first so a naive implementation that just
        // takes rows in insertion order would fail this test.
        var laterBatchId = await TestDb.AddBatchAsync(context, fixture, "LATE", _clock.UtcNow.AddMonths(12), quantity: 100);
        var soonerBatchId = await TestDb.AddBatchAsync(context, fixture, "SOON", _clock.UtcNow.AddMonths(2), quantity: 100);

        var result = await service.IssueStockFefoAsync(
            fixture.ItemId, fixture.WarehouseId, quantity: 30,
            referenceType: "SalesInvoice", referenceId: 1, performedByUserId: 1);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].BatchId.Should().Be(soonerBatchId);
        result.Value[0].QuantityTaken.Should().Be(30);

        (await context.Batches.AsNoTracking().FirstAsync(b => b.Id == soonerBatchId)).QuantityOnHand.Should().Be(70);
        (await context.Batches.AsNoTracking().FirstAsync(b => b.Id == laterBatchId)).QuantityOnHand.Should().Be(100);
    }

    [Fact]
    public async Task IssueStockFefo_SpansMultipleBatches_WhenOneIsNotEnough()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var service = new InventoryService(context, _clock);

        var soonerBatchId = await TestDb.AddBatchAsync(context, fixture, "SOON", _clock.UtcNow.AddMonths(2), quantity: 20);
        var laterBatchId = await TestDb.AddBatchAsync(context, fixture, "LATE", _clock.UtcNow.AddMonths(12), quantity: 50);

        var result = await service.IssueStockFefoAsync(
            fixture.ItemId, fixture.WarehouseId, quantity: 35,
            referenceType: "SalesInvoice", referenceId: 1, performedByUserId: 1);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        // The sooner-expiring batch must be drained completely before the later one is touched.
        result.Value![0].BatchId.Should().Be(soonerBatchId);
        result.Value[0].QuantityTaken.Should().Be(20);
        result.Value[1].BatchId.Should().Be(laterBatchId);
        result.Value[1].QuantityTaken.Should().Be(15);

        (await context.Batches.AsNoTracking().FirstAsync(b => b.Id == soonerBatchId)).QuantityOnHand.Should().Be(0);
        (await context.Batches.AsNoTracking().FirstAsync(b => b.Id == laterBatchId)).QuantityOnHand.Should().Be(35);
    }

    [Fact]
    public async Task IssueStockFefo_FailsWithoutDeductingAnything_WhenStockIsInsufficient()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var service = new InventoryService(context, _clock);

        var batchId = await TestDb.AddBatchAsync(context, fixture, "ONLY", _clock.UtcNow.AddMonths(6), quantity: 10);

        var result = await service.IssueStockFefoAsync(
            fixture.ItemId, fixture.WarehouseId, quantity: 25,
            referenceType: "SalesInvoice", referenceId: 1, performedByUserId: 1);

        result.Succeeded.Should().BeFalse();

        // The critical assertion: a failed issuance must be atomic. Partially deducting stock
        // here would silently corrupt inventory on every oversell attempt.
        (await context.Batches.AsNoTracking().FirstAsync(b => b.Id == batchId)).QuantityOnHand.Should().Be(10);
        (await context.StockTransactions.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task IssueStockFefo_WritesOneStockTransactionPerBatchTouched()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var service = new InventoryService(context, _clock);

        await TestDb.AddBatchAsync(context, fixture, "SOON", _clock.UtcNow.AddMonths(2), quantity: 20);
        await TestDb.AddBatchAsync(context, fixture, "LATE", _clock.UtcNow.AddMonths(12), quantity: 50);

        await service.IssueStockFefoAsync(
            fixture.ItemId, fixture.WarehouseId, quantity: 35,
            referenceType: "SalesInvoice", referenceId: 99, performedByUserId: 1);

        var transactions = await context.StockTransactions.AsNoTracking().ToListAsync();

        transactions.Should().HaveCount(2);
        transactions.Should().OnlyContain(t => t.Type == StockTransactionType.SaleIssue);
        transactions.Should().OnlyContain(t => t.ReferenceId == 99);
        transactions.Sum(t => t.QuantityChange).Should().Be(-35, "issuing stock must be recorded as a negative movement");
    }

    [Fact]
    public async Task RestockBatch_ReturnsQuantityToTheOriginalBatch()
    {
        await using var context = TestDb.CreateContext(_clock);
        var fixture = await TestDb.SeedBaselineAsync(context);
        var service = new InventoryService(context, _clock);

        var soonerBatchId = await TestDb.AddBatchAsync(context, fixture, "SOON", _clock.UtcNow.AddMonths(2), quantity: 20);
        var laterBatchId = await TestDb.AddBatchAsync(context, fixture, "LATE", _clock.UtcNow.AddMonths(12), quantity: 50);

        await service.IssueStockFefoAsync(
            fixture.ItemId, fixture.WarehouseId, quantity: 20,
            referenceType: "SalesInvoice", referenceId: 1, performedByUserId: 1);

        var result = await service.RestockBatchAsync(
            soonerBatchId, quantity: 5, referenceType: "SalesReturn", referenceId: 2, performedByUserId: 1);

        result.Succeeded.Should().BeTrue();

        // A return must go back to the batch it was sold from, not to whichever batch
        // happens to expire soonest now — otherwise batch-level expiry tracking drifts.
        (await context.Batches.AsNoTracking().FirstAsync(b => b.Id == soonerBatchId)).QuantityOnHand.Should().Be(5);
        (await context.Batches.AsNoTracking().FirstAsync(b => b.Id == laterBatchId)).QuantityOnHand.Should().Be(50);
    }
}
