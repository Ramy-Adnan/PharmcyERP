using Microsoft.EntityFrameworkCore;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;

namespace PharmacyERP.Application.Tests.Common;

/// <summary>
/// Builds an <see cref="ApplicationDbContext"/> backed by the EF Core InMemory
/// provider, with the real audit interceptor attached so soft-delete and audit
/// stamping behave exactly as they do in production.
///
/// Known and deliberate limitation: the InMemory provider does not enforce
/// relational constraints (unique indexes, FK integrity). These tests therefore
/// verify *business logic in the services* — uniqueness rules that the services
/// check explicitly are still covered, but a test passing here is not a claim
/// that the database schema would accept the same data.
/// </summary>
public static class TestDb
{
    public static ApplicationDbContext CreateContext(FakeDateTime clock, FakeCurrentUserService? currentUser = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .EnableSensitiveDataLogging()
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var interceptor = new AuditableEntitySaveChangesInterceptor(currentUser ?? new FakeCurrentUserService(), clock);
        return new ApplicationDbContext(options, interceptor);
    }

    /// <summary>
    /// Seeds the minimum graph every operational test needs: one branch, one warehouse,
    /// one category/unit, and one item. Returns the created ids so tests can reference them
    /// without re-querying.
    /// </summary>
    public static async Task<TestFixture> SeedBaselineAsync(ApplicationDbContext context, bool itemRequiresPrescription = false)
    {
        var branch = new Branch
        {
            Code = "MAIN",
            Name = "الفرع الرئيسي",
            Type = BranchType.MainPharmacy,
            IsMainBranch = true,
            IsActive = true
        };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            BranchId = branch.Id,
            Code = "MAIN-WH",
            Name = "المخزن الرئيسي",
            IsDefault = true,
            IsActive = true
        };
        context.Warehouses.Add(warehouse);

        var category = new ItemCategory { Code = "GEN", Name = "عام", IsActive = true };
        var unit = new UnitOfMeasure { Code = "PCS", Name = "قطعة", IsActive = true };
        context.ItemCategories.Add(category);
        context.UnitsOfMeasure.Add(unit);
        await context.SaveChangesAsync();

        var item = new Item
        {
            Code = "ITM-001",
            Name = "دواء تجريبي",
            CategoryId = category.Id,
            UnitOfMeasureId = unit.Id,
            RequiresPrescription = itemRequiresPrescription,
            DefaultSalePrice = 10m,
            DefaultPurchasePrice = 6m,
            TaxRatePercent = 0m,
            IsActive = true
        };
        context.Items.Add(item);
        await context.SaveChangesAsync();

        return new TestFixture
        {
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            CategoryId = category.Id,
            UnitOfMeasureId = unit.Id,
            ItemId = item.Id
        };
    }

    /// <summary>Adds a batch of the fixture's item with a given expiry and quantity, and returns its id.</summary>
    public static async Task<int> AddBatchAsync(
        ApplicationDbContext context, TestFixture fixture, string batchNumber,
        DateTime expiryDate, int quantity, decimal purchasePrice = 6m)
    {
        var batch = new Batch
        {
            ItemId = fixture.ItemId,
            WarehouseId = fixture.WarehouseId,
            BatchNumber = batchNumber,
            ExpiryDate = expiryDate,
            QuantityOnHand = quantity,
            PurchasePrice = purchasePrice,
            ReceivedAtUtc = DateTime.UtcNow
        };
        context.Batches.Add(batch);
        await context.SaveChangesAsync();
        return batch.Id;
    }
}

public class TestFixture
{
    public int BranchId { get; init; }
    public int WarehouseId { get; init; }
    public int CategoryId { get; init; }
    public int UnitOfMeasureId { get; init; }
    public int ItemId { get; init; }
}
