using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class ItemCataloguePagingTests
{
    private readonly FakeDateTime _clock = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PagedCatalogue_BoundsRowsAndKeepsAStableOrderWithNoTracking(bool relational)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using ApplicationDbContext db = relational ? new SqliteTestDb(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options,
            new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock)) : TestDb.CreateContext(_clock);
        if (relational) await db.Database.EnsureCreatedAsync();
        var f = await TestDb.SeedBaselineAsync(db);
        for (var i = 1; i <= 125; i++) db.Items.Add(new Item { Code = $"P{i:000}", Name = "نفس الاسم",
            CategoryId = f.CategoryId, UnitOfMeasureId = f.UnitOfMeasureId });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new InventoryService(db, _clock);
        var first = await service.GetItemsPageAsync("نفس الاسم");
        first.Items.Should().HaveCount(60); first.HasMore.Should().BeTrue(); first.NextSkip.Should().Be(60);
        var second = await service.GetItemsPageAsync("نفس الاسم", first.NextSkip);
        second.Items.Should().HaveCount(60); second.HasMore.Should().BeTrue();
        var last = await service.GetItemsPageAsync("نفس الاسم", second.NextSkip);
        last.Items.Should().HaveCount(5); last.HasMore.Should().BeFalse(); last.NextSkip.Should().Be(125);
        var ids = first.Items.Concat(second.Items).Concat(last.Items).Select(i => i.Id).ToList();
        ids.Should().OnlyHaveUniqueItems(); ids.Should().BeInAscendingOrder();
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ExactBarcodesAndCodes_AppearBeforePartialNameMatchesWithoutLosingLeadingZeros()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        db.Items.AddRange(
            new Item { Code = "NAME", Name = "A 001234", CategoryId = f.CategoryId, UnitOfMeasureId = f.UnitOfMeasureId },
            new Item { Code = "PACKAGE", Barcode = "001234", Name = "Z Package", CategoryId = f.CategoryId, UnitOfMeasureId = f.UnitOfMeasureId },
            new Item { Code = "001234", Name = "Z Code", CategoryId = f.CategoryId, UnitOfMeasureId = f.UnitOfMeasureId },
            new Item { Code = "EXTRA", Name = "Z Strip", CategoryId = f.CategoryId, UnitOfMeasureId = f.UnitOfMeasureId,
                SaleUnits = new List<ItemSaleUnit> { new() { Name = "شريط", BaseUnitCount = 20, Barcode = "001234" } } });
        await db.SaveChangesAsync();
        var service = new InventoryService(db, _clock);
        var first = await service.GetItemsPageAsync(" 001234 ", 0, 3);
        first.Items.Select(i => i.Code).Should().BeEquivalentTo("PACKAGE", "001234", "EXTRA");
        first.Items.Single(i => i.Code == "PACKAGE").Barcode.Should().Be("001234"); first.HasMore.Should().BeTrue();
        (await service.GetItemsPageAsync("001234", 3, 3)).Items.Single().Code.Should().Be("NAME");
    }

    [Fact]
    public async Task PageSizeIsClamped_AndNegativeOffsetStartsAtTheBeginning()
    {
        await using var db = TestDb.CreateContext(_clock); await TestDb.SeedBaselineAsync(db);
        var page = await new InventoryService(db, _clock).GetItemsPageAsync(null, -50, 0);
        page.Items.Should().ContainSingle(); page.NextSkip.Should().Be(1); page.HasMore.Should().BeFalse();
    }
}
