using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace PharmacyERP.Application.Tests;

public partial class PackagingSalesTests
{
    private SqliteTestDb FreshValidationDb(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options,
        new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock));

    [Theory]
    [InlineData("item", "غير نشط")]
    [InlineData("prescription", "وصفة")]
    [InlineData("unit", "وحدة البيع")]
    [InlineData("warehouse", "المخزن")]
    public async Task Checkout_RejectsRestrictionsChangedOnAnotherDeviceEvenWithTrackedOldEntities(string changed, string expectedError)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = FreshValidationDb(connection);
        await db.Database.EnsureCreatedAsync(); var f = await SeedAsync(db);
        var unit = new ItemSaleUnit { ItemId = f.ItemId, Name = "وحدة مزدوجة", BaseUnitCount = 2, Barcode = "DOUBLE" };
        db.ItemSaleUnits.Add(unit); await db.SaveChangesAsync();
        await ReceiveAsync(db, f);
        var cachedItem = await db.Items.SingleAsync();
        cachedItem.IsActive.Should().BeTrue(); cachedItem.RequiresPrescription.Should().BeFalse();
        var request = Request(f, changed != "unit");
        if (changed == "unit") request.Lines.Single().ItemSaleUnitId = unit.Id;

        // ExecuteUpdate models a committed update by another register without
        // refreshing this register's existing tracked item/unit/warehouse.
        switch (changed)
        {
            case "item":
                await db.Items.Where(i => i.Id == f.ItemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.IsActive, false));
                cachedItem.IsActive.Should().BeTrue(); break;
            case "prescription":
                await db.Items.Where(i => i.Id == f.ItemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.RequiresPrescription, true));
                cachedItem.RequiresPrescription.Should().BeFalse(); break;
            case "unit":
                await db.ItemSaleUnits.Where(u => u.Id == unit.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
                unit.IsActive.Should().BeTrue(); break;
            case "warehouse":
                await db.Warehouses.Where(w => w.Id == f.WarehouseId).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsActive, false));
                db.Warehouses.Local.Single().IsActive.Should().BeTrue(); break;
        }

        var result = await Sales(db).CheckoutAsync(request, 1);
        result.Succeeded.Should().BeFalse(); string.Join("; ", result.Errors).Should().Contain(expectedError);
        (await db.SalesInvoices.CountAsync()).Should().Be(0);
        (await db.Batches.AsNoTracking().SingleAsync()).QuantityOnHand.Should().Be(10);
        (await db.StockTransactions.CountAsync()).Should().Be(1, "only the original receipt may be recorded");
        (await db.JournalEntries.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("item", "غير نشط")]
    [InlineData("unit", "وحدة الاستلام")]
    [InlineData("warehouse", "المخزن")]
    public async Task Receipt_RejectsItemUnitOrWarehouseDisabledOnAnotherDevice(string changed, string expectedError)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = FreshValidationDb(connection);
        await db.Database.EnsureCreatedAsync(); var f = await SeedAsync(db);
        var unit = new ItemSaleUnit { ItemId = f.ItemId, Name = "وحدة مزدوجة", BaseUnitCount = 2 };
        db.ItemSaleUnits.Add(unit); await db.SaveChangesAsync();
        var request = new ReceiveBatchDto { ItemId = f.ItemId, WarehouseId = f.WarehouseId,
            BatchNumber = "NO-LONGER-VALID", Quantity = 2, PurchasePrice = 1000, ExpiryDate = _clock.UtcNow.AddMonths(6) };
        switch (changed)
        {
            case "item":
                await db.Items.Where(i => i.Id == f.ItemId).ExecuteUpdateAsync(s => s.SetProperty(i => i.IsActive, false));
                db.Items.Local.Single().IsActive.Should().BeTrue(); break;
            case "unit":
                request.ItemSaleUnitId = unit.Id;
                await db.ItemSaleUnits.Where(u => u.Id == unit.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
                unit.IsActive.Should().BeTrue(); break;
            case "warehouse":
                await db.Warehouses.Where(w => w.Id == f.WarehouseId).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsActive, false));
                db.Warehouses.Local.Single().IsActive.Should().BeTrue(); break;
        }
        var result = await Inventory(db).ReceiveBatchAsync(request, 1);
        result.Succeeded.Should().BeFalse(); string.Join("; ", result.Errors).Should().Contain(expectedError);
        (await db.Batches.CountAsync()).Should().Be(0); (await db.StockTransactions.CountAsync()).Should().Be(0);
    }
}
