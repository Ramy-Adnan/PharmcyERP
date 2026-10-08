using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Infrastructure.Migrations;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.ViewModels.Inventory;
using PharmacyERP.WPF.ViewModels.Purchasing;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class PurchasePricingTests
{
    private readonly FakeDateTime _clock = new();

    [Theory]
    [InlineData(1000, 1030)]
    [InlineData(2000, 2060)]
    [InlineData(10000, 10300)]
    public void EnteringCost_CalculatesThreePercentAndAllowsManualEditing(int purchase, int expected)
    {
        var row = new POLineRow { UnitCost = purchase, QuantityOrdered = 2 };
        row.SalePrice.Should().Be(expected);
        row.SalePrice = 1500; row.QuantityOrdered = 3;
        row.SalePrice.Should().Be(1500); row.LineTotal.Should().Be(purchase * 3);
        row.CalculateSalePriceCommand.Execute(null); row.SalePrice.Should().Be(expected);
        row.UnitCost = 500; row.SalePrice.Should().Be(515);
        var receiptRow = new GRLineRow { UnitCost = purchase, SalePrice = 1500, QuantityReceived = 2 };
        receiptRow.SalePrice.Should().Be(1500); receiptRow.LineTotal.Should().Be(purchase * 2);
        receiptRow.UnitCost = 500; receiptRow.SalePrice.Should().Be(515);
    }

    [Fact]
    public async Task ItemEditor_SavesSuggestedPriceAndPreservesExistingManualPriceOnReload()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await TestDb.SeedBaselineAsync(db);
        var inventory = new InventoryService(db, _clock); var editor = new ItemEditViewModel(inventory);
        await editor.LoadForCreateAsync(); editor.Code = "NEW"; editor.Name = "صنف جديد";
        editor.DefaultPurchasePrice = 1000; editor.DefaultSalePrice.Should().Be(1030);
        editor.DefaultSalePrice = 1200;
        await editor.SaveAsync(); editor.SavedSuccessfully.Should().BeTrue();
        var item = await db.Items.SingleAsync(i => i.Code == "NEW"); item.DefaultSalePrice.Should().Be(1200);
        var reload = new ItemEditViewModel(inventory); await reload.LoadForEditAsync(item.Id);
        reload.DefaultSalePrice.Should().Be(1200); reload.DefaultPurchasePrice.Should().Be(1000);
        await reload.SaveAsync(); (await db.Items.SingleAsync(i => i.Id == item.Id)).DefaultSalePrice.Should().Be(1200);
        reload.DefaultPurchasePrice = 2000; reload.DefaultSalePrice.Should().Be(2060);
        reload.DefaultSalePrice = 2500; reload.CalculateSalePriceCommand.Execute(null); reload.DefaultSalePrice.Should().Be(2060);
    }

    [Theory]
    [InlineData(null, 1030)]
    [InlineData(1200, 1200)]
    [InlineData(0, 0)]
    public async Task ItemService_UsesAutomaticPriceOnlyWhenNoOverrideIsProvided(int? price, int expected)
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var result = await new InventoryService(db, _clock).CreateItemAsync(new ItemUpsertDto
        {
            Code = "NEW", Name = "صنف جديد", CategoryId = f.CategoryId, UnitOfMeasureId = f.UnitOfMeasureId,
            DefaultPurchasePrice = 1000, DefaultSalePrice = price
        });
        result.Succeeded.Should().BeTrue(); result.Value!.DefaultSalePrice.Should().Be(expected);
    }

    [Fact]
    public async Task BatchEditor_StartsWithPurchaseCostPlusThreePercentAndSavesManualOverride()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var inventory = new InventoryService(db, _clock);
        var branches = new BranchService(db, new PermissiveLicenseService());
        var editor = new ReceiveBatchViewModel(inventory, branches, new FakeCurrentUserService());
        await editor.InitializeAsync(f.ItemId, f.BranchId);
        editor.PurchasePrice.Should().Be(6); editor.SalePriceOverride.Should().Be(6.18m);
        editor.PurchasePrice = 1000; editor.SalePriceOverride.Should().Be(1030);
        editor.SalePriceOverride = 1200; editor.Quantity = 2; editor.BatchNumber = "MANUAL";
        editor.ExpiryDate = _clock.UtcNow.AddMonths(3);
        await editor.SaveAsync(); editor.SavedSuccessfully.Should().BeTrue();
        var batch = await db.Batches.SingleAsync(); batch.PurchasePrice.Should().Be(1000); batch.SalePriceOverride.Should().Be(1200);
        var lookup = await Sales(db, inventory).SearchSaleItemsAsync("ITM-001", f.WarehouseId);
        lookup.Single().DefaultSalePrice.Should().Be(1200);
    }

    [Fact]
    public async Task BatchService_RoundsAutomaticPriceToTwoDecimalsAndRejectsNegativePrices()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var inventory = new InventoryService(db, _clock);
        var dto = new ReceiveBatchDto { ItemId = f.ItemId, WarehouseId = f.WarehouseId, BatchNumber = "ROUND",
            ExpiryDate = _clock.UtcNow.AddMonths(3), Quantity = 2, PurchasePrice = 0.50m };
        (await inventory.ReceiveBatchAsync(dto, null)).Value!.SalePriceOverride.Should().Be(0.52m);
        SalePricePolicy.FromPurchasePrice(0.50m).Should().Be(0.52m);
        dto.PurchasePrice = -1; (await inventory.ReceiveBatchAsync(dto, null)).Succeeded.Should().BeFalse();
        dto.PurchasePrice = 10; dto.SalePriceOverride = -1;
        (await inventory.ReceiveBatchAsync(dto, null)).Succeeded.Should().BeFalse();
        (await db.Batches.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(null, 1030)]
    [InlineData(1200, 1200)]
    [InlineData(0, 0)]
    public async Task OrderService_PersistsSuggestedOrManualPriceAndReturnsItForReceiving(int? price, int expected)
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var supplier = await SupplierAsync(db); var inventory = new InventoryService(db, _clock); var purchasing = Purchasing(db, inventory);
        var request = Order(f, supplier); request.Lines[0].SalePrice = price;
        var result = await purchasing.CreatePurchaseOrderAsync(request); result.Succeeded.Should().BeTrue();
        (await purchasing.GetPurchaseOrderForEditAsync(result.Value!.Id))!.Lines.Single().SalePrice.Should().Be(expected);
        (await purchasing.GetOutstandingLinesForReceiptAsync(result.Value.Id)).Single().SalePrice.Should().Be(expected);
        request.Id = result.Value.Id; request.Lines[0].SalePrice = 1300;
        (await purchasing.UpdatePurchaseOrderAsync(request)).Succeeded.Should().BeTrue();
        (await purchasing.GetOutstandingLinesForReceiptAsync(result.Value.Id)).Single().SalePrice.Should().Be(1300);
        request.Lines[0].SalePrice = -1; (await purchasing.UpdatePurchaseOrderAsync(request)).Succeeded.Should().BeFalse();
        (await purchasing.GetPurchaseOrderForEditAsync(result.Value.Id))!.Lines.Single().SalePrice.Should().Be(1300);
    }

    [Fact]
    public async Task OrderEditor_ToGoodsReceiptToStockAndPos_KeepsTheSelectedSalePrice()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var supplier = await SupplierAsync(db); var inventory = new InventoryService(db, _clock); var purchasing = Purchasing(db, inventory);
        var branches = new BranchService(db, new PermissiveLicenseService());
        var editor = new PurchaseOrderEditViewModel(purchasing, inventory, branches);
        await editor.LoadForCreateAsync(); editor.SupplierId = supplier; editor.WarehouseId = f.WarehouseId;
        editor.OrderDate = _clock.UtcNow.Date;
        var row = new POLineRow { ItemId = f.ItemId, QuantityOrdered = 2, UnitCost = 1000, SalePrice = 1200 };
        editor.Lines.Add(row); await editor.SaveAsync(); editor.SavedSuccessfully.Should().BeTrue();
        var orderId = (await db.PurchaseOrders.SingleAsync()).Id;
        var reopen = new PurchaseOrderEditViewModel(purchasing, inventory, branches); await reopen.LoadForEditAsync(orderId);
        reopen.Lines.Single().SalePrice.Should().Be(1200);
        (await purchasing.SubmitPurchaseOrderAsync(orderId)).Succeeded.Should().BeTrue();
        var receipt = new GoodsReceiptEditViewModel(purchasing, inventory, branches);
        await receipt.LoadForCreateAsync(); receipt.SupplierId = supplier; receipt.PurchaseOrderId = orderId;
        await receipt.LoadFromPurchaseOrderAsync();
        var received = receipt.Lines.Single(); received.SalePrice.Should().Be(1200);
        received.BatchNumber = "FROM-PO"; received.ExpiryDate = _clock.UtcNow.AddMonths(3); receipt.ReceiptDate = _clock.UtcNow.Date;
        await receipt.SaveAsync(); receipt.SavedSuccessfully.Should().BeTrue();
        var noteId = (await db.GoodsReceiptNotes.SingleAsync()).Id;
        var reopenedReceipt = new GoodsReceiptEditViewModel(purchasing, inventory, branches); await reopenedReceipt.LoadForEditAsync(noteId);
        reopenedReceipt.Lines.Single().SalePrice.Should().Be(1200);
        (await purchasing.PostGoodsReceiptAsync(noteId, null)).Succeeded.Should().BeTrue();
        (await db.Batches.SingleAsync()).SalePriceOverride.Should().Be(1200);
        var lookup = (await Sales(db, inventory).SearchSaleItemsAsync("ITM-001", f.WarehouseId)).Single();
        lookup.DefaultSalePrice.Should().Be(1200); lookup.AvailableQuantity.Should().Be(2);
    }

    [Theory]
    [InlineData(null, 1030)]
    [InlineData(1200, 1200)]
    [InlineData(0, 0)]
    public async Task ManualGoodsReceipt_TransfersSuggestedOrExplicitPriceToStockAndPos(int? price, int expected)
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var supplier = await SupplierAsync(db); var inventory = new InventoryService(db, _clock); var purchasing = Purchasing(db, inventory);
        var result = await purchasing.CreateGoodsReceiptAsync(new()
        {
            SupplierId = supplier, BranchId = f.BranchId, WarehouseId = f.WarehouseId, ReceiptDate = _clock.UtcNow.Date,
            Lines = new() { new() { ItemId = f.ItemId, BatchNumber = "MANUAL-GR", ExpiryDate = _clock.UtcNow.AddMonths(3), QuantityReceived = 2, UnitCost = 1000, SalePrice = price } }
        });
        result.Succeeded.Should().BeTrue(); (await db.GoodsReceiptItems.SingleAsync()).SalePrice.Should().Be(expected);
        (await purchasing.PostGoodsReceiptAsync(result.Value!.Id, null)).Succeeded.Should().BeTrue();
        var batch = await db.Batches.SingleAsync();
        batch.SalePriceOverride.Should().Be(expected); batch.HasConfiguredSalePrice.Should().BeTrue();
        (await Sales(db, inventory).SearchSaleItemsAsync("ITM-001", f.WarehouseId)).Single().DefaultSalePrice.Should().Be(expected);
    }

    [Fact]
    public async Task Pos_UsesPriceOfTheActualFefoBatchAndFallsBackForLegacyBatches()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var expiry = _clock.UtcNow.AddMonths(3);
        var later = await TestDb.AddBatchAsync(db, f, "LATER", expiry, 2);
        var first = await TestDb.AddBatchAsync(db, f, "FIRST", expiry, 2);
        var firstBatch = await db.Batches.FindAsync(first); firstBatch!.ReceivedAtUtc = _clock.UtcNow.AddDays(-1); firstBatch.SalePriceOverride = 1200;
        var laterBatch = await db.Batches.FindAsync(later); laterBatch!.ReceivedAtUtc = _clock.UtcNow; laterBatch.SalePriceOverride = 1500;
        var expired = await TestDb.AddBatchAsync(db, f, "EXPIRED", _clock.UtcNow.AddDays(-1), 2);
        (await db.Batches.FindAsync(expired))!.SalePriceOverride = 1;
        await db.SaveChangesAsync(); var inventory = new InventoryService(db, _clock); var sales = Sales(db, inventory);
        (await sales.SearchSaleItemsAsync("ITM-001", f.WarehouseId)).Single().DefaultSalePrice.Should().Be(1200);
        var allocated = await inventory.IssueStockFefoAsync(f.ItemId, f.WarehouseId, 2, "Test", null, null);
        allocated.Value!.Single().BatchId.Should().Be(first);
        (await sales.SearchSaleItemsAsync("ITM-001", f.WarehouseId)).Single().DefaultSalePrice.Should().Be(1500);
        laterBatch.SalePriceOverride = null; await db.SaveChangesAsync();
        (await sales.SearchSaleItemsAsync("ITM-001", f.WarehouseId)).Single().DefaultSalePrice.Should().Be(10);
        laterBatch.SalePriceOverride = 0; await db.SaveChangesAsync();
        (await sales.SearchSaleItemsAsync("ITM-001", f.WarehouseId)).Single().DefaultSalePrice.Should().Be(10);
        laterBatch.HasConfiguredSalePrice = true; await db.SaveChangesAsync();
        (await sales.SearchSaleItemsAsync("ITM-001", f.WarehouseId)).Single().DefaultSalePrice.Should().Be(0);
    }

    [Fact]
    public async Task AddSalePriceMigration_PreservesExistingOrdersAndLeavesTheirPriceUnset()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options, new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock));
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE PurchaseOrderItems (Id INTEGER PRIMARY KEY, UnitCost decimal(18,2)); INSERT INTO PurchaseOrderItems VALUES (1, 1000); CREATE TABLE Batches (Id INTEGER PRIMARY KEY, SalePriceOverride decimal(18,2)); INSERT INTO Batches VALUES (1, 0);");
        var migration = new AddPurchaseOrderSalePrice();
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations);
        foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await using var read = connection.CreateCommand(); read.CommandText = "SELECT Id, UnitCost, SalePrice FROM PurchaseOrderItems";
        await using var reader = await read.ExecuteReaderAsync(); (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(1); reader.GetDecimal(1).Should().Be(1000); reader.IsDBNull(2).Should().BeTrue();
        await reader.DisposeAsync();
        read.CommandText = "SELECT SalePriceOverride, HasConfiguredSalePrice FROM Batches";
        await using var batchReader = await read.ExecuteReaderAsync(); (await batchReader.ReadAsync()).Should().BeTrue();
        batchReader.GetDecimal(0).Should().Be(0); batchReader.GetBoolean(1).Should().BeFalse();
    }

    private PurchasingService Purchasing(ApplicationDbContext db, InventoryService inventory) =>
        new(db, inventory, new AccountingService(db, _clock), _clock);
    private SalesService Sales(ApplicationDbContext db, InventoryService inventory) =>
        new(db, inventory, new AccountingService(db, _clock), _clock, new FakeCurrentUserService());
    private static async Task<int> SupplierAsync(ApplicationDbContext db)
    {
        var supplier = new Supplier { Code = "S1", Name = "المورد", IsActive = true };
        db.Suppliers.Add(supplier); await db.SaveChangesAsync(); return supplier.Id;
    }
    private PurchaseOrderUpsertDto Order(TestFixture f, int supplierId) => new()
    {
        SupplierId = supplierId, BranchId = f.BranchId, WarehouseId = f.WarehouseId, OrderDate = _clock.UtcNow.Date,
        Lines = new() { new() { ItemId = f.ItemId, QuantityOrdered = 2, UnitCost = 1000 } }
    };
}
