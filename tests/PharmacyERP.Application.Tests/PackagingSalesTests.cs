using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Features.Purchasing.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.WPF.ViewModels.Sales;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class PackagingSalesTests
{
    private readonly FakeDateTime _clock = new();
    private sealed class TestPrinter : PharmacyERP.WPF.Services.IReceiptPrinter
    { public void Print(SalesInvoiceDetailDto invoice) { } }
    private InventoryService Inventory(ApplicationDbContext db) => new(db, _clock);
    private SalesService Sales(ApplicationDbContext db) => new(db, Inventory(db), new AccountingService(db, _clock), _clock, new FakeCurrentUserService());
    private async Task<TestFixture> SeedAsync(ApplicationDbContext db, int factor = 5)
    {
        var f = await TestDb.SeedBaselineAsync(db);
        var item = await db.Items.SingleAsync();
        item.Name = "باراسيتامول"; item.GenericName = "Paracetamol"; item.Strength = "500 mg";
        item.Barcode = "1234567890"; item.BaseUnitBarcode = "0987654321";
        item.UnitsPerPackage = factor; item.PackageUnitName = "علبة";
        item.DefaultPurchasePrice = 8000; item.DefaultSalePrice = 10000;
        (await db.UnitsOfMeasure.SingleAsync()).Name = "شريط";
        db.Manufacturers.Add(new Manufacturer { Name = "الشركة الأولى" });
        db.Roles.Add(new Role { Name = "Cashier" });
        await db.SaveChangesAsync();
        item.ManufacturerId = (await db.Manufacturers.SingleAsync()).Id;
        db.Users.Add(new User { Username = "cashier", FullName = "Cashier", PasswordHash = "test", RoleId = (await db.Roles.SingleAsync()).Id, DefaultBranchId = f.BranchId });
        db.Customers.Add(new Customer { Name = "Customer", Code = "C1", IsActive = true });
        db.Suppliers.Add(new Supplier { Name = "Supplier", Code = "S1", IsActive = true });
        foreach (var code in new[] { "1110", "1120", "1160", "4100", "2200", "1130", "1140", "2100" })
            db.ChartOfAccounts.Add(new ChartOfAccount { Name = code, Code = code, IsActive = true });
        await db.SaveChangesAsync(); return f;
    }
    private async Task<BatchDto> ReceiveAsync(ApplicationDbContext db, TestFixture f, int boxes = 2, string batch = "B1", int days = 100, decimal sale = 10000)
    {
        var result = await Inventory(db).ReceiveBatchAsync(new() { ItemId = f.ItemId, WarehouseId = f.WarehouseId,
            BatchNumber = batch, Quantity = boxes, PurchasePrice = 8000, SalePriceOverride = sale, ExpiryDate = _clock.UtcNow.AddDays(days) }, 1);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors)); return result.Value!;
    }
    private SalesCheckoutDto Request(TestFixture f, bool package, int quantity = 1) => new()
    {
        BranchId = f.BranchId, WarehouseId = f.WarehouseId, PaymentMethod = PaymentMethod.Cash,
        Lines = new() { new() { ItemId = f.ItemId, Quantity = quantity, SellAsPackage = package, UnitPrice = package ? 10000 : 2000 } }
    };
    private async Task<POSViewModel> PosAsync(ApplicationDbContext db, TestFixture f)
    {
        var current = new FakeCurrentUserService(); current.SetSession(1, "cashier", f.BranchId, Array.Empty<string>());
        var pos = new POSViewModel(Sales(db), new BranchService(db, new PermissiveLicenseService()), new PrescriptionService(db, _clock), current, new TestPrinter());
        await pos.InitializeAsync(); return pos;
    }

    [Fact]
    public async Task Receive_StoresTenStripsAndCostPerStrip_WhileLookupKeepsWholeBoxPrice()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var batch = await ReceiveAsync(db, f); batch.QuantityOnHand.Should().Be(10); batch.PurchasePrice.Should().Be(1600);
        batch.SalePriceOverride.Should().Be(2000); batch.UnitOfMeasureName.Should().Be("شريط");
        (await db.StockTransactions.SingleAsync()).QuantityChange.Should().Be(10);
        var lookup = (await Sales(db).SearchSaleItemsAsync("Paracetamol", f.WarehouseId)).Single();
        lookup.PackageSalePrice.Should().Be(10000); lookup.DefaultSalePrice.Should().Be(2000); lookup.AvailableQuantity.Should().Be(10);
        lookup.DisplayName.Should().Contain("الشركة الأولى").And.Contain("500 mg");
        (await Sales(db).SearchSaleItemsAsync("0987654321", f.WarehouseId)).Single().ItemId.Should().Be(f.ItemId);
    }

    [Theory]
    [InlineData(false, 1, 9, 2000, "شريط")]
    [InlineData(true, 1, 5, 10000, "علبة")]
    [InlineData(true, 2, 0, 20000, "علبة")]
    public async Task CheckoutAndReturn_UseTheSoldUnitForMoneyAndTheBaseUnitForStock(bool package, int quantity, int remaining, int total, string name)
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f);
        var sales = Sales(db); var result = await sales.CheckoutAsync(Request(f, package, quantity), 1);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors)); result.Value!.TotalAmount.Should().Be(total);
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(remaining);
        var detail = (await sales.GetSalesInvoiceDetailAsync(result.Value.Id))!; var line = detail.Lines.Single();
        line.Quantity.Should().Be(quantity); line.UnitName.Should().Be(name); line.UnitsPerSale.Should().Be(package ? 5 : 1);
        line.ItemName.Should().Contain("الشركة الأولى");
        (await db.SalesInvoiceItemBatches.SumAsync(a => a.QuantityTaken * a.UnitCost)).Should().Be((10 - remaining) * 1600);
        var returned = await sales.ProcessReturnAsync(new() { SalesInvoiceId = result.Value.Id, Reason = "اختبار", Lines = new() { new() { SalesInvoiceItemId = line.Id, Quantity = 1 } } }, 1);
        returned.Succeeded.Should().BeTrue(); returned.Value!.TotalAmount.Should().Be(package ? 10000 : 2000);
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(remaining + (package ? 5 : 1));
        if (quantity == 2)
        {
            var second = await sales.ProcessReturnAsync(new() { SalesInvoiceId = result.Value.Id, Reason = "إرجاع الباقي", Lines = new() { new() { SalesInvoiceItemId = line.Id, Quantity = 1 } } }, 1);
            second.Succeeded.Should().BeTrue(); (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(10);
        }
    }

    [Fact]
    public async Task PackagePrice_IsPreservedWhenDivisionNeedsRounding_AndCostRetainsPrecision()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db, 3); await ReceiveAsync(db, f);
        var lookup = (await Sales(db).SearchSaleItemsAsync("1234567890", f.WarehouseId)).Single();
        lookup.DefaultSalePrice.Should().Be(3333.33m); lookup.PackageSalePrice.Should().Be(10000);
        (await db.Batches.SingleAsync()).PurchasePrice.Should().Be(2666.666667m);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        pos.CartLines.Single().UnitPrice.Should().Be(10000);
        pos.CartLines.Single().SelectedSaleUnit = pos.CartLines.Single().SaleUnits.First();
        pos.CartLines.Single().UnitPrice.Should().Be(3333.33m);
    }

    [Fact]
    public async Task Pos_SwitchToStripChangesPrice_RepeatedScannerEventsAddStrips_AndCheckoutPrintsSelectedUnit()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        var row = pos.CartLines.Single(); row.UnitPrice.Should().Be(10000); row.AvailableQuantity.Should().Be(2);
        row.SelectedSaleUnit = row.SaleUnits.First(); row.UnitPrice.Should().Be(2000); row.AvailableQuantity.Should().Be(10);
        await Task.WhenAll(pos.SubmitSearchAsync("1234567890", true), pos.SubmitSearchAsync("1234567890", true));
        row.Quantity.Should().Be(3); pos.TotalAmount.Should().Be(6000);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty(); (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(7);
        var detail = (await Sales(db).GetSalesInvoiceDetailAsync((await db.SalesInvoices.SingleAsync()).Id))!;
        var content = PharmacyERP.WPF.Services.ReceiptContentBuilder.Build(detail, new());
        content.Products.Single().Single().Value.Should().Contain("3 شريط");
    }

    [Fact]
    public async Task Pos_BaseBarcodeCanAddStripBesideBox_AndBothShareStock()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true); await pos.SubmitSearchAsync("0987654321", true);
        pos.CartLines.Should().HaveCount(2); pos.TotalAmount.Should().Be(12000);
        pos.CartLines.Single(l => !l.SellAsPackage).Quantity.Should().Be(1);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty(); (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(4);
    }

    [Fact]
    public async Task Pos_AddOtherUnitAndPartBoxFallback_KeepTheRemainingStripsSellable()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true);
        pos.AddOtherUnitCommand.Execute(null); pos.CartLines.Should().HaveCount(2); pos.TotalAmount.Should().Be(12000);
        await pos.CheckoutAsync(); (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(4);
        await pos.SubmitSearchAsync("1234567890", true); var line = pos.CartLines.Single();
        line.SellAsPackage.Should().BeFalse(); line.UnitPrice.Should().Be(2000);
        line.SelectedSaleUnit = line.SaleUnits.Last(); await pos.CheckoutAsync(); pos.ErrorMessage.Should().NotBeEmpty();
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(4);
    }

    [Fact]
    public async Task MixedUnitsCannotOversell_AndDuplicateSameUnitIsRefusedBeforeWrites()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f, 1);
        var request = Request(f, true); request.Lines.Add(new() { ItemId = f.ItemId, Quantity = 1, UnitPrice = 2000 });
        (await Sales(db).CheckoutAsync(request, 1)).Succeeded.Should().BeFalse();
        (await db.SalesInvoices.CountAsync()).Should().Be(0); (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(5);
        request.Lines[0].SellAsPackage = false;
        (await Sales(db).CheckoutAsync(request, 1)).Succeeded.Should().BeFalse(); (await db.SalesInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Void_RestoresAllBaseUnits_AndRetryDoesNotRestockTwice()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f);
        var sales = Sales(db); var request = Request(f, true); var sale = await sales.CheckoutAsync(request, 1);
        (await sales.CheckoutAsync(request, 1)).Value!.Id.Should().Be(sale.Value!.Id);
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(5);
        (await sales.CancelSalesInvoiceAsync(sale.Value.Id, 1)).Succeeded.Should().BeTrue();
        (await sales.CancelSalesInvoiceAsync(sale.Value.Id, 1)).Succeeded.Should().BeTrue();
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(10);
    }

    [Fact]
    public async Task DifferentCompaniesHaveIndependentPricesStockAndBarcodeResults()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f);
        db.Manufacturers.Add(new Manufacturer { Name = "الشركة الثانية" }); await db.SaveChangesAsync();
        var card = (await Inventory(db).GetItemForEditAsync(f.ItemId))!; card.Id = null; card.Code = "COMPANY-2";
        card.Barcode = "2222222222"; card.BaseUnitBarcode = null; card.DefaultSalePrice = 15000;
        card.ManufacturerId = (await db.Manufacturers.SingleAsync(m => m.Name == "الشركة الثانية")).Id;
        var created = await Inventory(db).CreateItemAsync(card); created.Succeeded.Should().BeTrue();
        var results = await Sales(db).SearchSaleItemsAsync("Paracetamol", f.WarehouseId); results.Should().HaveCount(2);
        results.Single(i => i.ItemId == created.Value!.Id).PackageSalePrice.Should().Be(15000);
        results.Single(i => i.ItemId == created.Value!.Id).AvailableQuantity.Should().Be(0);
        (await Sales(db).SearchSaleItemsAsync("الشركة الثانية", f.WarehouseId)).Single().ItemId.Should().Be(created.Value!.Id);
        (await Sales(db).CheckoutAsync(Request(f, false), 1)).Succeeded.Should().BeTrue();
        (await db.Batches.SingleAsync()).ItemId.Should().Be(f.ItemId);
    }

    [Fact]
    public async Task PackagingIsEditableBeforeUse_ThenFrozenEvenAfterAllStockIsSold()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var inventory = Inventory(db); var card = (await inventory.GetItemForEditAsync(f.ItemId))!;
        card.UnitsPerPackage = 4; (await inventory.UpdateItemAsync(card)).Succeeded.Should().BeTrue();
        await ReceiveAsync(db, f, 1); (await Sales(db).CheckoutAsync(Request(f, true), 1)).Succeeded.Should().BeTrue();
        card.UnitsPerPackage = 5; var change = await inventory.UpdateItemAsync(card);
        change.Succeeded.Should().BeFalse(); change.Errors.Single().Should().Contain("حركات سابقة");
        (await db.Items.SingleAsync()).UnitsPerPackage.Should().Be(4);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(100001)]
    public async Task InvalidPackagingIsRejected(int factor)
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var card = (await Inventory(db).GetItemForEditAsync(f.ItemId))!; card.UnitsPerPackage = factor;
        (await Inventory(db).UpdateItemAsync(card)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task BarcodesCannotCollideAcrossCompaniesOrBetweenBoxAndStrip()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var inventory = Inventory(db); var card = (await inventory.GetItemForEditAsync(f.ItemId))!;
        card.Id = null; card.Code = "NEW"; card.Barcode = "0987654321"; card.BaseUnitBarcode = null;
        (await inventory.CreateItemAsync(card)).Succeeded.Should().BeFalse();
        card.Id = f.ItemId; card.BaseUnitBarcode = card.Barcode;
        (await inventory.UpdateItemAsync(card)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task GoodsReceiptAndSupplierInvoiceStayInBoxesWhileStockIsInStrips()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var purchases = new PurchasingService(db, Inventory(db), new AccountingService(db, _clock), _clock);
        var receipt = await purchases.CreateGoodsReceiptAsync(new() { BranchId = f.BranchId, WarehouseId = f.WarehouseId,
            SupplierId = (await db.Suppliers.SingleAsync()).Id, ReceiptDate = _clock.UtcNow.Date,
            Lines = new() { new() { ItemId = f.ItemId, QuantityReceived = 2, UnitCost = 8000, SalePrice = 10000,
                BatchNumber = "PURCHASE", ExpiryDate = _clock.UtcNow.AddMonths(4) } } });
        receipt.Succeeded.Should().BeTrue(); (await purchases.PostGoodsReceiptAsync(receipt.Value!.Id, 1)).Succeeded.Should().BeTrue();
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(10);
        var invoiceDto = (await purchases.PrefillInvoiceFromGoodsReceiptAsync(receipt.Value.Id))!;
        invoiceDto.Lines.Single().Quantity.Should().Be(2); invoiceDto.Lines.Single().UnitCost.Should().Be(8000);
        var invoice = await purchases.CreatePurchaseInvoiceAsync(invoiceDto);
        invoice.Succeeded.Should().BeTrue(); invoice.Value!.TotalAmount.Should().Be(16000); invoice.Value.AmountDue.Should().Be(16000);
    }

    [Fact]
    public async Task PrescriptionLimitsAndReturnsAreInSmallUnits_ForBoxSalesAsWell()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f);
        (await db.Items.SingleAsync()).RequiresPrescription = true;
        db.Doctors.Add(new Doctor { FullName = "Doctor" }); await db.SaveChangesAsync();
        var rx = new Prescription { Number = "RX1", CustomerId = 1, DoctorId = 1, BranchId = f.BranchId,
            PrescriptionDate = _clock.UtcNow.Date, Status = PrescriptionStatus.Active,
            Items = new List<PrescriptionItem> { new() { ItemId = f.ItemId, QuantityPrescribed = 4 } } };
        db.Prescriptions.Add(rx); await db.SaveChangesAsync();
        var request = Request(f, true); request.CustomerId = 1; request.PrescriptionId = rx.Id;
        var sales = Sales(db); (await sales.CheckoutAsync(request, 1)).Succeeded.Should().BeFalse();
        rx.Items.Single().QuantityPrescribed = 10; await db.SaveChangesAsync();
        var sale = await sales.CheckoutAsync(request, 1); sale.Succeeded.Should().BeTrue(); rx.Items.Single().QuantityDispensed.Should().Be(5);
        (await sales.CancelSalesInvoiceAsync(sale.Value!.Id, 1)).Succeeded.Should().BeTrue(); rx.Items.Single().QuantityDispensed.Should().Be(0);
        request.RequestId = Guid.NewGuid(); var again = await sales.CheckoutAsync(request, 1);
        var line = (await sales.GetSalesInvoiceDetailAsync(again.Value!.Id))!.Lines.Single();
        (await sales.ProcessReturnAsync(new() { SalesInvoiceId = again.Value.Id, Reason = "Test", Lines = new() { new() { SalesInvoiceItemId = line.Id, Quantity = 1 } } }, 1)).Succeeded.Should().BeTrue();
        rx.Items.Single().QuantityDispensed.Should().Be(0); (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(10);
    }
    [Fact]
    public async Task RelationalCheckout_UsesTwoFefoBatches_AndRollsBackWhenReturnProviderFails()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new SqliteTestDb(options, new PharmacyERP.Infrastructure.Persistence.Interceptors.AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(), _clock));
        await db.Database.EnsureCreatedAsync(); var f = await SeedAsync(db);
        var early = await ReceiveAsync(db, f, 1, "EARLY", 30);
        (await Inventory(db).AdjustStockAsync(new() { BatchId = early.Id, QuantityDelta = -2, Reason = "Count adjustment" }, 1)).Succeeded.Should().BeTrue();
        var later = await ReceiveAsync(db, f, 1, "LATER", 90);
        var sales = Sales(db); var result = await sales.CheckoutAsync(Request(f, true), 1);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors));
        var allocations = await db.SalesInvoiceItemBatches.OrderBy(a => a.BatchId).ToListAsync();
        allocations.Select(a => a.QuantityTaken).Should().Equal(3, 2);
        (await db.Batches.FindAsync(early.Id))!.QuantityOnHand.Should().Be(0);
        (await db.Batches.FindAsync(later.Id))!.QuantityOnHand.Should().Be(3);
        var line = (await sales.GetSalesInvoiceDetailAsync(result.Value!.Id))!.Lines.Single();
        // SQLite cannot SUM decimal return amounts. Exercise the real rollback after
        // stock has been restocked but before the return can finish; SQL Server supports this aggregate.
        Func<Task> returnAttempt = () => sales.ProcessReturnAsync(new() { SalesInvoiceId = result.Value.Id, Reason = "Test", Lines = new() { new() { SalesInvoiceItemId = line.Id, Quantity = 1 } } }, 1);
        await returnAttempt.Should().ThrowAsync<NotSupportedException>();
        db.ChangeTracker.Clear();
        (await db.Batches.FindAsync(early.Id))!.QuantityOnHand.Should().Be(0);
        (await db.Batches.FindAsync(later.Id))!.QuantityOnHand.Should().Be(3);
        (await db.SalesInvoiceItems.SingleAsync()).QuantityReturned.Should().Be(0);
        (await db.SalesReturns.CountAsync()).Should().Be(0);
        (await db.SalesInvoiceItemBatches.ToListAsync()).Should().OnlyContain(a => a.RestockedQuantity == 0);
    }

    [Fact]
    public async Task MixedBoxAndStripMustRespectTheirCombinedPrescriptionLimit()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f);
        (await db.Items.SingleAsync()).RequiresPrescription = true; db.Doctors.Add(new Doctor { FullName = "Doctor" });
        await db.SaveChangesAsync();
        var rx = new Prescription { Number = "RX1", CustomerId = 1, DoctorId = 1, BranchId = f.BranchId, Status = PrescriptionStatus.Active,
            Items = new List<PrescriptionItem> { new() { ItemId = f.ItemId, QuantityPrescribed = 6 } } };
        db.Prescriptions.Add(rx); await db.SaveChangesAsync();
        var request = Request(f, true); request.CustomerId = 1; request.PrescriptionId = rx.Id;
        request.Lines.Add(new() { ItemId = f.ItemId, Quantity = 2, UnitPrice = 2000 });
        (await Sales(db).CheckoutAsync(request, 1)).Succeeded.Should().BeFalse();
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(10); (await db.SalesInvoices.CountAsync()).Should().Be(0);
        request.Lines[1].Quantity = 1;
        (await Sales(db).CheckoutAsync(request, 1)).Succeeded.Should().BeTrue(); rx.Items.Single().QuantityDispensed.Should().Be(6);
    }

    [Fact]
    public async Task LegacySingleUnit_ReceivesAndSellsWithoutReinterpretingOldStockOrPrices()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await TestDb.SeedBaselineAsync(db);
        var result = await Inventory(db).ReceiveBatchAsync(new() { ItemId = f.ItemId, WarehouseId = f.WarehouseId, Quantity = 2,
            PurchasePrice = 8000, SalePriceOverride = 10000, BatchNumber = "LEGACY", ExpiryDate = _clock.UtcNow.AddMonths(3) }, null);
        result.Succeeded.Should().BeTrue(); result.Value!.QuantityOnHand.Should().Be(2); result.Value.PurchasePrice.Should().Be(8000);
        var lookup = (await Sales(db).SearchSaleItemsAsync("ITM-001", f.WarehouseId)).Single();
        lookup.UnitsPerPackage.Should().Be(1); lookup.DefaultSalePrice.Should().Be(10000);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("ITM-001");
        pos.CartLines.Single().SaleUnits.Should().ContainSingle(); pos.CartLines.Single().UnitPrice.Should().Be(10000);
        pos.AddOtherUnitCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task BoxReturn_RestoresOriginalFefoBatches_IncludingAPartiallyOpenedBox()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db);
        var early = await ReceiveAsync(db, f, 1, "EARLY", 30);
        (await Inventory(db).AdjustStockAsync(new() { BatchId = early.Id, QuantityDelta = -2, Reason = "Count adjustment" }, 1)).Succeeded.Should().BeTrue();
        var later = await ReceiveAsync(db, f, 1, "LATER", 90);
        var sales = Sales(db); var result = await sales.CheckoutAsync(Request(f, true), 1);
        var line = (await sales.GetSalesInvoiceDetailAsync(result.Value!.Id))!.Lines.Single();
        var returned = await sales.ProcessReturnAsync(new() { SalesInvoiceId = result.Value.Id, Reason = "Test", Lines = new() { new() { SalesInvoiceItemId = line.Id, Quantity = 1 } } }, 1);
        returned.Succeeded.Should().BeTrue();
        (await db.Batches.FindAsync(early.Id))!.QuantityOnHand.Should().Be(3);
        (await db.Batches.FindAsync(later.Id))!.QuantityOnHand.Should().Be(5);
        (await db.SalesInvoiceItems.SingleAsync()).QuantityReturned.Should().Be(1);
    }

    [Fact]
    public async Task Scanner_WithBoxAndStripRows_FollowsTheCashierSelectedRow()
    {
        await using var db = TestDb.CreateContext(_clock); var f = await SeedAsync(db); await ReceiveAsync(db, f, 3);
        var pos = await PosAsync(db, f); await pos.SubmitSearchAsync("1234567890", true); pos.AddOtherUnitCommand.Execute(null);
        var box = pos.CartLines.Single(l => l.SellAsPackage); var strip = pos.CartLines.Single(l => !l.SellAsPackage);
        pos.SelectedCartLine = box; await pos.SubmitSearchAsync("1234567890", true);
        box.Quantity.Should().Be(2); strip.Quantity.Should().Be(1);
        pos.SelectedCartLine = strip; await pos.SubmitSearchAsync("1234567890", true);
        box.Quantity.Should().Be(2); strip.Quantity.Should().Be(2); pos.TotalAmount.Should().Be(24000);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty(); (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(3);
    }

}
