using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class InventoryAtomicReceiptTests
{
    private sealed class FailStockInsert : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO \"StockTransactions\""))
                throw new IOException("Injected stock ledger failure");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task FailedReceiptLedgerInsert_RollsBackTheBatchAndDoesNotLeakIntoTheNextSave()
    {
        var clock = new FakeDateTime();
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var fault = new FailStockInsert();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).AddInterceptors(fault).Options;
        await using var db = new SqliteTestDb(options, new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), clock));
        await db.Database.EnsureCreatedAsync(); var f = await TestDb.SeedBaselineAsync(db);
        var inventory = new InventoryService(db, clock);
        var request = new PharmacyERP.Application.Features.Inventory.DTOs.ReceiveBatchDto
        { ItemId = f.ItemId, WarehouseId = f.WarehouseId, BatchNumber = "ATOMIC", Quantity = 2, PurchasePrice = 1000, ExpiryDate = clock.UtcNow.AddMonths(6) };
        fault.Enabled = true;
        await FluentActions.Awaiting(() => inventory.ReceiveBatchAsync(request, null)).Should().ThrowAsync<DbUpdateException>();
        fault.Enabled = false;
        await db.SaveChangesAsync(); // An unrelated later save must not resurrect the failed lot.
        (await db.Batches.CountAsync()).Should().Be(0); (await db.StockTransactions.CountAsync()).Should().Be(0);
        var retried = await inventory.ReceiveBatchAsync(request, null);
        retried.Succeeded.Should().BeTrue();
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(2);
        var movement = await db.StockTransactions.SingleAsync(); movement.QuantityChange.Should().Be(2);
        movement.BatchId.Should().Be(retried.Value!.Id);
    }
}
