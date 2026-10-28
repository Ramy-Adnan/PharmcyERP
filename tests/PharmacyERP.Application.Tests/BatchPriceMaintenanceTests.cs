using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public partial class PackagingSalesTests
{
    [Fact]
    public async Task BatchPriceMaintenance_UpdatesFutureUnitPricesAndAuditWithoutRewritingCostStockOrCompletedSale()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db);
        var received = await ReceiveAsync(db, fixture, sale: 1250);
        var pos = await PosAsync(db, fixture); await pos.SubmitSearchAsync("1234567890", true);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        var sold = await db.SalesInvoiceItems.SingleAsync(); sold.UnitPrice.Should().Be(1250);
        var service = new InventoryService(db, _clock, new FakeCurrentUserService());
        var batch = (await service.GetBatchesForItemAsync(fixture.ItemId)).Single();
        batch.SaleUnitFactor.Should().Be(5); batch.SaleUnitName.Should().Be("علبة");
        var quantityBefore = batch.QuantityOnHand; var costBefore = batch.PurchasePrice;
        var stockMovementCount = await db.StockTransactions.CountAsync();
        var result = await service.UpdateBatchSalePriceAsync(new() { BatchId = received.Id, SaleUnitPrice = 2500,
            ExpectedSaleUnitPrice = batch.SaleUnitPrice, ExpectedRowVersion = batch.RowVersion, Reason = "تحديث قائمة الأسعار" });
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value!.SaleUnitPrice.Should().Be(2500); result.Value.SalePriceOverride.Should().Be(500);
        var stored = await db.Batches.AsNoTracking().SingleAsync();
        stored.QuantityOnHand.Should().Be(quantityBefore); stored.PurchasePrice.Should().Be(costBefore);
        (await db.StockTransactions.CountAsync()).Should().Be(stockMovementCount);
        (await db.SalesInvoiceItems.AsNoTracking().SingleAsync()).UnitPrice.Should().Be(1250);
        var future = (await Sales(db).SearchSaleItemsAsync("1234567890", fixture.WarehouseId)).Single();
        future.PackageSalePrice.Should().Be(2500); future.DefaultSalePrice.Should().Be(500);
        var audit = await db.AuditLogs.SingleAsync(); audit.EntityId.Should().Be(received.Id.ToString());
        audit.UserId.Should().Be(1); audit.OldValuesJson.Should().Contain("1250"); audit.NewValuesJson.Should().Contain("2500");
        System.Text.Json.JsonDocument.Parse(audit.NewValuesJson!).RootElement.GetProperty("Reason").GetString().Should().Be("تحديث قائمة الأسعار");
    }

    [Fact]
    public async Task BatchPriceMaintenance_RejectsStaleEditorInsteadOfOverwritingAnotherChange()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db);
        await ReceiveAsync(db, fixture, sale: 1250);
        var service = new InventoryService(db, _clock, new FakeCurrentUserService());
        var initial = (await service.GetBatchesForItemAsync(fixture.ItemId)).Single();
        var request = new UpdateBatchSalePriceDto { BatchId = initial.Id, ExpectedSaleUnitPrice = initial.SaleUnitPrice,
            ExpectedRowVersion = initial.RowVersion, SaleUnitPrice = 2000, Reason = "تحديث" };
        (await service.UpdateBatchSalePriceAsync(request)).Succeeded.Should().BeTrue();
        request.SaleUnitPrice = 3000;
        var stale = await service.UpdateBatchSalePriceAsync(request);
        stale.Succeeded.Should().BeFalse(); stale.Errors.Should().Contain(e => e.Contains("تغيّرت"));
        (await db.Batches.AsNoTracking().SingleAsync()).PackageSalePrice.Should().Be(2000);
        (await db.AuditLogs.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(0, "تحديث")]
    [InlineData(-1, "تحديث")]
    [InlineData(2500, "")]
    [InlineData(0.001, "تحديث")]
    public async Task BatchPriceMaintenance_RejectsInvalidPriceOrMissingReasonWithoutSideEffects(decimal price, string reason)
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db);
        await ReceiveAsync(db, fixture, sale: 1250);
        var service = new InventoryService(db, _clock, new FakeCurrentUserService());
        var initial = (await service.GetBatchesForItemAsync(fixture.ItemId)).Single();
        var result = await service.UpdateBatchSalePriceAsync(new() { BatchId = initial.Id, ExpectedSaleUnitPrice = initial.SaleUnitPrice,
            ExpectedRowVersion = initial.RowVersion, SaleUnitPrice = price, Reason = reason });
        result.Succeeded.Should().BeFalse(); (await db.AuditLogs.CountAsync()).Should().Be(0);
        (await db.Batches.AsNoTracking().SingleAsync()).PackageSalePrice.Should().Be(1250);
    }

    [Fact]
    public async Task BatchPriceMaintenance_EnforcesManageItemsPermissionInsideService()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db);
        var batch = await ReceiveAsync(db, fixture, sale: 1250);
        var service = new InventoryService(db, _clock, new PriceMaintenanceUser());
        var result = await service.UpdateBatchSalePriceAsync(new() { BatchId = batch.Id, ExpectedSaleUnitPrice = 1250,
            SaleUnitPrice = 2500, Reason = "تحديث" });
        result.Succeeded.Should().BeFalse(); result.Errors.Should().Contain(e => e.Contains("صلاحية"));
        (await db.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BatchPriceMaintenance_FailedSaveDoesNotLeakPriceOrAuditIntoLaterWrites()
    {
        await using var db = new PriceSaveFailureContext(_clock); var fixture = await SeedAsync(db);
        await ReceiveAsync(db, fixture, sale: 1250);
        var service = new InventoryService(db, _clock, new FakeCurrentUserService());
        var initial = (await service.GetBatchesForItemAsync(fixture.ItemId)).Single();
        db.FailNextSave = true;
        await FluentActions.Awaiting(() => service.UpdateBatchSalePriceAsync(new() { BatchId = initial.Id,
            ExpectedSaleUnitPrice = initial.SaleUnitPrice, ExpectedRowVersion = initial.RowVersion, SaleUnitPrice = 2500, Reason = "اختبار فشل الحفظ" }))
            .Should().ThrowAsync<InvalidOperationException>();
        db.ChangeTracker.Entries<PharmacyERP.Domain.Entities.AuditLog>().Should().BeEmpty();
        (await db.Items.SingleAsync()).Name = "اسم معدل"; await db.SaveChangesAsync();
        (await db.Batches.AsNoTracking().SingleAsync()).PackageSalePrice.Should().Be(1250);
        (await db.AuditLogs.CountAsync()).Should().Be(0);
    }

    private sealed class PriceSaveFailureContext : PharmacyERP.Infrastructure.Persistence.ApplicationDbContext
    {
        public bool FailNextSave { get; set; }
        public PriceSaveFailureContext(FakeDateTime clock) : base(
            new DbContextOptionsBuilder<PharmacyERP.Infrastructure.Persistence.ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new PharmacyERP.Infrastructure.Persistence.Interceptors.AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), clock)) { }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSave) { FailNextSave = false; throw new InvalidOperationException("Injected save failure"); }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task BatchPriceMaintenance_RejectsExpiredOrEmptyStock()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db);
        await ReceiveAsync(db, fixture, sale: 1250);
        var stored = await db.Batches.SingleAsync(); stored.QuantityOnHand = 0; await db.SaveChangesAsync();
        var service = new InventoryService(db, _clock, new FakeCurrentUserService());
        var request = new UpdateBatchSalePriceDto { BatchId = stored.Id, ExpectedSaleUnitPrice = 1250, SaleUnitPrice = 2500, Reason = "تحديث" };
        (await service.UpdateBatchSalePriceAsync(request)).Succeeded.Should().BeFalse();
        stored.QuantityOnHand = 5; stored.ExpiryDate = _clock.UtcNow.AddDays(-1); await db.SaveChangesAsync();
        (await service.UpdateBatchSalePriceAsync(request)).Succeeded.Should().BeFalse();
        (await db.AuditLogs.CountAsync()).Should().Be(0);
    }

    private sealed class PriceMaintenanceUser : ICurrentUserService
    {
        public int? UserId => 1; public string? UserName => "cashier"; public int? CurrentBranchId => 1;
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
        public bool HasPermission(string code) => false;
        public void SetSession(int id, string name, int branch, IEnumerable<string> permissions) { }
        public void ClearSession() { }
    }
}
