using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class CatalogueReadTests
{
    private readonly FakeDateTime _clock = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ItemCatalogue_ReturnsStockAndAllUnitsWithoutTrackingTheBatchHistory(bool relational)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using ApplicationDbContext db = relational ? new SqliteTestDb(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options,
            new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock)) : TestDb.CreateContext(_clock);
        if (relational) await db.Database.EnsureCreatedAsync();
        var f = await TestDb.SeedBaselineAsync(db);
        var item = await db.Items.SingleAsync(); item.UnitsPerPackage = 100; item.PackageUnitName = "علبة";
        db.ItemSaleUnits.AddRange(
            new ItemSaleUnit { ItemId = item.Id, Name = "شريط", BaseUnitCount = 20, Barcode = "STRIP001" },
            new ItemSaleUnit { ItemId = item.Id, Name = "شريط قديم", BaseUnitCount = 10, Barcode = "INACTIVE", IsActive = false });
        for (var i = 1; i <= 40; i++) db.Batches.Add(new Batch { ItemId = f.ItemId, WarehouseId = f.WarehouseId,
            BatchNumber = "B" + i, QuantityOnHand = i, PurchasePrice = 1000, ExpiryDate = _clock.UtcNow.AddMonths(i) });
        db.Items.Add(new Item { Code = "EMPTY", Name = "بلا دفعات", CategoryId = f.CategoryId, UnitOfMeasureId = f.UnitOfMeasureId });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();

        var inventory = new InventoryService(db, _clock);
        var list = await inventory.GetItemsAsync();
        var found = list.Single(i => i.Id == f.ItemId);
        found.TotalQuantityOnHand.Should().Be(820); found.SaleUnits.Should().HaveCount(2);
        found.PurchaseUnits.Select(u => u.BaseUnitCount).Should().Equal(100, 20);
        list.Single(i => i.Code == "EMPTY").TotalQuantityOnHand.Should().Be(0);
        (await inventory.GetItemsAsync("STRIP001")).Single().Id.Should().Be(f.ItemId);
        (await inventory.GetItemsAsync("INACTIVE")).Should().BeEmpty();
        db.ChangeTracker.Entries().Should().BeEmpty("catalogue reads must not retain every item and historical batch");
    }

    [Fact]
    public async Task SupplierCatalogue_KeepsBalancesAndOpenOrderCountsWithoutLoadingDocumentGraphs()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var supplier = new Supplier { Code = "SUP", Name = "المذخر" }; db.Suppliers.Add(supplier); await db.SaveChangesAsync();
        foreach (var state in new[] { PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted, PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Received })
            db.PurchaseOrders.Add(new PurchaseOrder { Number = state.ToString(), SupplierId = supplier.Id,
                BranchId = f.BranchId, WarehouseId = f.WarehouseId, Status = state });
        db.PurchaseInvoices.AddRange(
            new PurchaseInvoice { Number = "PI1", SupplierId = supplier.Id, BranchId = f.BranchId, TotalAmount = 2000, AmountPaid = 500 },
            new PurchaseInvoice { Number = "PI2", SupplierId = supplier.Id, BranchId = f.BranchId, TotalAmount = 1000, AmountPaid = 1000, Status = PurchaseInvoiceStatus.Paid },
            new PurchaseInvoice { Number = "VOID", SupplierId = supplier.Id, BranchId = f.BranchId, TotalAmount = 9000, Status = PurchaseInvoiceStatus.Cancelled });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new PurchasingService(db, new InventoryService(db, _clock), new AccountingService(db, _clock), _clock);
        var result = (await service.GetSuppliersAsync()).Single();
        result.OpenPurchaseOrderCount.Should().Be(2); result.TotalOutstandingBalance.Should().Be(1500);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }
}
