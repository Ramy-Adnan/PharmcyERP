using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Features.Inventory.DTOs;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.Infrastructure.Services.Imports;
using PharmacyERP.WPF.ViewModels.Purchasing;
using PharmacyERP.WPF.ViewModels.Sales;
using Xunit;
namespace PharmacyERP.Application.Tests;
public class InvoiceImageImportTests
{
    private readonly FakeDateTime _clock = new() { UtcNow = new(2026,10,8,10,0,0,DateTimeKind.Utc) };
    private InventoryService Inventory(ApplicationDbContext db) => new(db, _clock);
    private PurchasingService Purchases(ApplicationDbContext db) => new(db, Inventory(db), new AccountingService(db, _clock), _clock);
    private PurchaseImageImportService Import(ApplicationDbContext db) => new(db, Inventory(db), Purchases(db), new FakeCurrentUserService(), _clock);
    private SalesService Sales(ApplicationDbContext db) => new(db, Inventory(db), new AccountingService(db,_clock),_clock,new FakeCurrentUserService());
    private async Task<TestFixture> Seed(ApplicationDbContext db)
    {
        var f = await TestDb.SeedBaselineAsync(db);
        (await db.UnitsOfMeasure.SingleAsync()).Name = "حبة";
        var item = await db.Items.SingleAsync(); item.UnitsPerPackage = 20; item.PackageUnitName = "علبة"; item.Name = "Paracetamol 1g 20 Tab Cisen Qidu";
        item.DefaultPurchasePrice = 3900; item.DefaultSalePrice = 4875; item.Barcode = "BOX"; item.BaseUnitBarcode = "TABLET";
        db.Roles.Add(new Role { Name = "Admin" }); await db.SaveChangesAsync();
        db.Users.Add(new User { Username = "cashier", FullName = "Cashier", PasswordHash = "test", RoleId = 1, DefaultBranchId = f.BranchId });
        db.Suppliers.Add(new Supplier { Name = "مذخر الإسراء", Code = "S", IsActive = true });
        foreach (var code in new[] { "1110", "1120", "1160", "4100", "2200", "1130", "1140", "2100" }) db.ChartOfAccounts.Add(new ChartOfAccount { Code=code,Name=code,IsActive=true });
        await db.SaveChangesAsync(); return f;
    }
    private PurchaseImageImportRequest Request(TestFixture f, int? existing = null, string source = "Paracetamol 1g 20 Tab Cisen Qidu") => new()
    {
        SupplierId=1,BranchId=f.BranchId,WarehouseId=f.WarehouseId,SupplierInvoiceNumber="15275",InvoiceDate=new(2026,10,7),SourceHash=new string('A',64),ParsedTotal=39000,ReviewedTotal=39000,
        Lines=new() { new() { ExistingItemId=existing, SourceName=source,ItemName=source,BaseUnitOfMeasureId=f.UnitOfMeasureId,CategoryId=f.CategoryId,ReceiveUnitName="علبة",BaseUnitsPerReceiveUnit=20,
            Quantity=10,BonusQuantity=0,UnitCost=3900,SalePrice=4875,BatchNumber="ACTUAL-BATCH",ExpiryDate=new(2028,4,1),Reviewed=true } }
    };
    [Fact]
    public async Task NewMedicineWithoutManufacturer_IsDraftOnly_UntilPosting_AndSupplierDebtStaysAtInvoiceValue()
    {
        await using var db=TestDb.CreateContext(_clock); var f=await Seed(db); var request=Request(f,null,"New paracetamol 20 Tab");
        var result=await Import(db).SaveReviewedAsync(request); result.Succeeded.Should().BeTrue(string.Join(";",result.Errors));
        var item=await db.Items.SingleAsync(i=>i.Id!=f.ItemId); item.ManufacturerId.Should().BeNull(); item.UnitsPerPackage.Should().Be(20);
        (await db.Batches.CountAsync()).Should().Be(0); (await db.GoodsReceiptNotes.SingleAsync()).Status.Should().Be(GoodsReceiptStatus.Draft);
        var purchases=Purchases(db); (await purchases.PostGoodsReceiptAsync(result.Value!.GoodsReceiptId,1)).Succeeded.Should().BeTrue();
        var batch=await db.Batches.SingleAsync(); batch.QuantityOnHand.Should().Be(200); batch.PurchasePrice.Should().Be(195); batch.SalePriceOverride.Should().Be(243.75m);
        var invoiceDto=(await purchases.PrefillInvoiceFromGoodsReceiptAsync(result.Value.GoodsReceiptId))!; invoiceDto.InvoiceDate.Should().Be(new DateTime(2026,10,7));
        var invoice=await purchases.CreatePurchaseInvoiceAsync(invoiceDto); invoice.Succeeded.Should().BeTrue(); invoice.Value!.AmountDue.Should().Be(39000);
        var matched=await Import(db).MatchAsync(1,new[] { new InvoiceImageLine { Name=request.Lines[0].SourceName } }); matched.Single().ItemId.Should().Be(item.Id);
    }
    [Theory]
    [InlineData(PurchasePricingType.Other,4875)]
    [InlineData(PurchasePricingType.ByHand,4680)]
    public async Task ExistingMedicine_GetsANewBatchAndMarkup_WithoutCreatingAnotherCard(PurchasePricingType type,int salePrice)
    {
        await using var db=TestDb.CreateContext(_clock); var f=await Seed(db); var request=Request(f,f.ItemId); request.PurchaseType=type; request.Lines[0].SalePrice=null;
        var first=await Import(db).SaveReviewedAsync(request); first.Succeeded.Should().BeTrue();
        (await Purchases(db).PostGoodsReceiptAsync(first.Value!.GoodsReceiptId,1)).Succeeded.Should().BeTrue();
        request.RequestId=Guid.NewGuid(); request.SourceHash=new string('B',64); request.SupplierInvoiceNumber="15276"; request.Lines[0].BatchNumber="NEW-BATCH";
        var second=await Import(db).SaveReviewedAsync(request); second.Succeeded.Should().BeTrue();
        (await Purchases(db).PostGoodsReceiptAsync(second.Value!.GoodsReceiptId,1)).Succeeded.Should().BeTrue();
        (await db.Items.CountAsync()).Should().Be(1); (await db.Batches.CountAsync()).Should().Be(2);
        (await db.Batches.ToListAsync()).Should().OnlyContain(b=>b.PackageSalePrice==salePrice && b.QuantityOnHand==200);
    }
    [Fact]
    public async Task BonusIncreasesStockButNotDebt_AndItsCostIsSpreadAcrossAllReceivedPieces()
    {
        await using var db=TestDb.CreateContext(_clock); var f=await Seed(db); var request=Request(f,f.ItemId);request.Lines[0].BonusQuantity=2;
        var result=await Import(db).SaveReviewedAsync(request);result.Succeeded.Should().BeTrue();
        var purchases=Purchases(db);(await purchases.PostGoodsReceiptAsync(result.Value!.GoodsReceiptId,1)).Succeeded.Should().BeTrue();
        var batch=await db.Batches.SingleAsync();batch.QuantityOnHand.Should().Be(240);batch.PurchasePrice.Should().Be(162.5m);batch.PackageSalePrice.Should().Be(4875);
        var dto=(await purchases.PrefillInvoiceFromGoodsReceiptAsync(result.Value.GoodsReceiptId))!;dto.Lines.Single().Quantity.Should().Be(10);dto.Lines.Single().BonusQuantity.Should().Be(2);
        var invoice=await purchases.CreatePurchaseInvoiceAsync(dto);invoice.Succeeded.Should().BeTrue();invoice.Value!.TotalAmount.Should().Be(39000);
    }
    [Theory]
    [InlineData("request")][InlineData("hash")][InlineData("number")]
    public async Task ReimportIsIdempotent_BeforeAndAfterPosting(string key)
    {
        await using var db=TestDb.CreateContext(_clock);var f=await Seed(db);var request=Request(f,f.ItemId);var importer=Import(db);
        var original=await importer.SaveReviewedAsync(request);original.Succeeded.Should().BeTrue();
        if(key!="request")request.RequestId=Guid.NewGuid();if(key=="number")request.SourceHash=new string('C',64);if(key=="hash")request.SupplierInvoiceNumber="DIFFERENT";
        var duplicate=await importer.SaveReviewedAsync(request);duplicate.Succeeded.Should().BeTrue();duplicate.Value!.AlreadyImported.Should().BeTrue();duplicate.Value.GoodsReceiptId.Should().Be(original.Value!.GoodsReceiptId);
        (await Purchases(db).PostGoodsReceiptAsync(original.Value.GoodsReceiptId,1)).Succeeded.Should().BeTrue();
        (await importer.SaveReviewedAsync(request)).Value!.AlreadyImported.Should().BeTrue();
        (await Purchases(db).PostGoodsReceiptAsync(original.Value.GoodsReceiptId,1)).Succeeded.Should().BeFalse();
        (await db.GoodsReceiptNotes.CountAsync()).Should().Be(1);(await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(200);
    }
    [Theory]
    [InlineData("review")][InlineData("total")][InlineData("unit")][InlineData("batch")][InlineData("expiry")][InlineData("warehouse")]
    public async Task IncompleteOrInconsistentReview_IsRejectedBeforeAnyWrite(string problem)
    {
        await using var db=TestDb.CreateContext(_clock);var f=await Seed(db);var request=Request(f,null,"NEW");
        switch(problem){case "review":request.Lines[0].Reviewed=false;break;case "total":request.ReviewedTotal++;break;case "unit":request.Lines[0].BaseUnitsPerReceiveUnit=0;break;
            case "batch":request.Lines[0].BatchNumber="";break;case "expiry":request.Lines[0].ExpiryDate=_clock.UtcNow;break;case "warehouse":request.WarehouseId=999;break;}
        (await Import(db).SaveReviewedAsync(request)).Succeeded.Should().BeFalse();(await db.Items.CountAsync()).Should().Be(1);(await db.GoodsReceiptNotes.CountAsync()).Should().Be(0);(await db.PurchaseImageImports.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task ChangedPackaging_AppendsAUnit_AndPreservesOldStockAndHistoricalFactors()
    {
        await using var db=TestDb.CreateContext(_clock);var f=await Seed(db);await Inventory(db).ReceiveBatchAsync(new(){ItemId=f.ItemId,WarehouseId=f.WarehouseId,Quantity=1,PurchasePrice=3900,BatchNumber="OLD",ExpiryDate=_clock.UtcNow.AddMonths(3)},1);
        var request=Request(f,f.ItemId);request.Lines[0].BaseUnitsPerReceiveUnit=30;request.Lines[0].Quantity=1;request.ReviewedTotal=3900;
        var result=await Import(db).SaveReviewedAsync(request);result.Succeeded.Should().BeTrue(string.Join(";",result.Errors));
        var editor=new GoodsReceiptEditViewModel(Purchases(db),Inventory(db),new BranchService(db,new PermissiveLicenseService()));
        await editor.LoadForEditAsync(result.Value!.GoodsReceiptId);var savedRow=editor.Lines.Single();var chosenUnit=savedRow.ItemSaleUnitId;
        chosenUnit.Should().NotBeNull();editor.ApplyItemSelection(savedRow,f.ItemId);savedRow.ItemSaleUnitId.Should().Be(chosenUnit);savedRow.UnitCost.Should().Be(3900);
        (await Purchases(db).PostGoodsReceiptAsync(result.Value!.GoodsReceiptId,1)).Succeeded.Should().BeTrue();
        (await db.Items.SingleAsync()).UnitsPerPackage.Should().Be(20);var extra=await db.ItemSaleUnits.SingleAsync();extra.BaseUnitCount.Should().Be(30);
        (await db.Batches.Select(b=>b.QuantityOnHand).ToListAsync()).Should().BeEquivalentTo(new[]{20,30});
        var card=(await Inventory(db).GetItemForEditAsync(f.ItemId))!;card.SaleUnits.Single().BaseUnitCount=40;
        (await Inventory(db).UpdateItemAsync(card)).Succeeded.Should().BeFalse();card.SaleUnits.Single().BaseUnitCount=30;
        card.SaleUnits.Add(new(){Name="شريط",BaseUnitCount=10,Barcode="STRIP"});(await Inventory(db).UpdateItemAsync(card)).Succeeded.Should().BeTrue();
    }
    [Fact]
    public async Task ArbitraryStripBarcode_AddsQuantity_AndBaseBarcodeTargetsTheTabletBesideIt()
    {
        await using var db=TestDb.CreateContext(_clock);var f=await Seed(db);var inventory=Inventory(db);var card=(await inventory.GetItemForEditAsync(f.ItemId))!;
        card.SaleUnits.Add(new(){Name="شريط",BaseUnitCount=10,Barcode="STRIP"});(await inventory.UpdateItemAsync(card)).Succeeded.Should().BeTrue();
        await inventory.ReceiveBatchAsync(new(){ItemId=f.ItemId,WarehouseId=f.WarehouseId,Quantity=2,PurchasePrice=3900,BatchNumber="B",ExpiryDate=_clock.UtcNow.AddMonths(4)},1);
        var pos=new POSViewModel(Sales(db),new BranchService(db,new PermissiveLicenseService()),new PrescriptionService(db,_clock),new FakeCurrentUserService(),new Printer());await pos.InitializeAsync();
        await pos.SubmitSearchAsync("STRIP",true);await pos.SubmitSearchAsync("STRIP",true);
        var strip=pos.CartLines.Single();strip.Quantity.Should().Be(2);strip.UnitPrice.Should().Be(2437.5m);strip.UnitsPerSale.Should().Be(10);
        await pos.SubmitSearchAsync("TABLET",true);pos.CartLines.Should().HaveCount(2);pos.CartLines.Single(l=>l.ItemSaleUnitId is null).Quantity.Should().Be(1);
        await pos.CheckoutAsync();pos.ErrorMessage.Should().BeEmpty();(await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(19);
        var detail=(await Sales(db).GetSalesInvoiceDetailAsync((await db.SalesInvoices.SingleAsync()).Id))!;var sold=detail.Lines.Single(l=>l.UnitName=="شريط");sold.UnitsPerSale.Should().Be(10);
        var returned=await Sales(db).ProcessReturnAsync(new(){SalesInvoiceId=detail.Header.Id,Reason="Test",Lines=new(){new(){SalesInvoiceItemId=sold.Id,Quantity=1}}},1);returned.Succeeded.Should().BeTrue();(await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(29);
    }
    private sealed class Printer : PharmacyERP.WPF.Services.IReceiptPrinter {public void Print(SalesInvoiceDetailDto invoice){} }
    [Fact]
    public async Task UnitBarcodeCollision_IsRejectedEvenIfAnExistingUnitIsOmittedFromTheEdit()
    {
        await using var db=TestDb.CreateContext(_clock);var f=await Seed(db);var inventory=Inventory(db);var card=(await inventory.GetItemForEditAsync(f.ItemId))!;
        card.SaleUnits.Add(new(){Name="شريط",BaseUnitCount=10,Barcode="STRIP"});(await inventory.UpdateItemAsync(card)).Succeeded.Should().BeTrue();
        card=(await inventory.GetItemForEditAsync(f.ItemId))!;card.SaleUnits.Clear();card.Barcode="STRIP";(await inventory.UpdateItemAsync(card)).Succeeded.Should().BeFalse();
    }
    [Fact]
    public async Task SqliteImport_IsAtomic_WhenASecondRowsAliasConflictsAfterCreatingTheFirstCard()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new SqliteTestDb(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options,new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(),_clock));
        await db.Database.EnsureCreatedAsync();var f=await Seed(db);
        db.SupplierItemAliases.Add(new(){SupplierId=1,ItemId=f.ItemId,SourceName="CONFLICT",NormalizedName="conflict"});await db.SaveChangesAsync();
        var request=Request(f,null,"FIRST NEW ITEM");var second=Request(f,null,"CONFLICT").Lines[0];request.Lines.Add(second);request.ReviewedTotal=78000;
        var result=await Import(db).SaveReviewedAsync(request);result.Succeeded.Should().BeFalse();db.ChangeTracker.Clear();
        (await db.Items.CountAsync()).Should().Be(1);(await db.SupplierItemAliases.CountAsync()).Should().Be(1);(await db.GoodsReceiptNotes.CountAsync()).Should().Be(0);(await db.PurchaseImageImports.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task SqliteImport_CommitsDraftWithRealConstraints_AndRetryFindsTheSameReceipt()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new SqliteTestDb(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options,new AuditableEntitySaveChangesInterceptor(new FakeCurrentUserService(),_clock));
        await db.Database.EnsureCreatedAsync();var f=await Seed(db);var request=Request(f,null,"NEW");var importer=Import(db);
        var first=await importer.SaveReviewedAsync(request);first.Succeeded.Should().BeTrue(string.Join(";",first.Errors));db.ChangeTracker.Clear();
        var again=await importer.SaveReviewedAsync(request);again.Succeeded.Should().BeTrue();again.Value!.AlreadyImported.Should().BeTrue();(await db.Items.CountAsync()).Should().Be(2);(await db.GoodsReceiptNotes.CountAsync()).Should().Be(1);
    }
    [Fact]
    public void Matcher_AmbiguityAndSimilarNamesAreSuggestions_WhileConfirmedSupplierAliasesWin()
    {
        var unit=new UnitOfMeasure{Name="حبة"};var one=new Item{Id=1,Name="Paracetamol 20 Tab",IsActive=true,UnitOfMeasure=unit};var two=new Item{Id=2,Name=one.Name,IsActive=true,UnitOfMeasure=unit};
        var exact=InvoiceItemMatcher.Match(new(){Name=one.Name},new[]{one,two},null);exact.ItemId.Should().BeNull();exact.Candidates.Should().HaveCount(2);
        var fuzzy=InvoiceItemMatcher.Match(new(){Name="Paracetamol 30 Tab"},new[]{one,two},null);fuzzy.ItemId.Should().BeNull();fuzzy.Candidates.Should().HaveCount(2);
        InvoiceItemMatcher.Match(new(){Name=one.Name},new[]{one,two},2).ItemId.Should().Be(2);
    }
    [Fact]
    public void ReviewChangingUnitOrPriceInvalidatesConfirmation_AndMarkupRemainsEditable()
    {
        var row=new InvoiceImageReviewRow{UnitCost=3900,BaseUnitsPerReceiveUnit=20};row.BasePurchasePrice.Should().Be(195);row.BaseSalePrice.Should().Be(243.75m);row.SalePrice.Should().Be(4875);
        row.Reviewed=true;row.BaseUnitsPerReceiveUnit=10;row.Reviewed.Should().BeFalse();row.Reviewed=true;row.PurchaseType=PurchasePricingType.ByHand;row.Reviewed.Should().BeFalse();row.SalePrice.Should().Be(4680);
        row.SalePrice=5000;row.BaseSalePrice.Should().Be(500);row.Reviewed=true;row.BatchNumber="Actual";row.Reviewed.Should().BeFalse();
    }
    [Fact]
    public void PurchaseUnitSelection_StartsOnPrimaryAndConvertsThePrice_WhenChangingToAStrip()
    {
        var primary=new PurchaseUnitOption(null,"علبة",20);var strip=new PurchaseUnitOption(7,"شريط",10);
        var row=new GRLineRow { PurchaseUnits=new[]{primary,strip},UnitCost=3900 };
        row.SelectedPurchaseUnit.Should().Be(primary);row.SelectedPurchaseUnit=strip;row.ItemSaleUnitId.Should().Be(7);row.UnitCost.Should().Be(1950);row.SalePrice.Should().Be(2437.5m);
        row.SelectedPurchaseUnit=primary;row.ItemSaleUnitId.Should().BeNull();row.UnitCost.Should().Be(3900);row.SelectedPurchaseUnit=null;row.SelectedPurchaseUnit.Should().Be(primary);
    }
    [Theory]
    [InlineData("15270",418368)] [InlineData("15275",631649)] [InlineData("15259",295539)]
    public async Task TranscribedPhotoAmounts_ExcludeBonusAndSupplierBalance_FromTheInvoiceTotal(string number,int expected)
    {
        // Manually transcribed printed monetary columns from the supplied photos;
        // these are accounting fixtures, not a claim of a live vision API test.
        var pairs=number switch {
            "15270"=>new[]{(2,0,10314),(5,0,4380),(5,1,8778),(5,1,9437),(5,1,8340),(5,1,15340),(5,1,2930),(5,1,10699),(5,1,19644)},
            "15275"=>new[]{(10,0,3900),(2,0,9000),(10,0,2000),(10,1,9326),(5,0,15134),(2,0,17384),(2,0,17384),(5,0,15339),(5,0,13805),(1,0,17895),(48,0,1254),(10,0,3862),(10,5,1320),(1,0,19000),(1,0,7036),(10,0,1452)},
            _=>new[]{(100,0,420),(24,0,598),(25,0,878),(80,0,1536),(25,0,585),(2,0,10636),(2,0,6585),(2,0,20451),(2,0,2194)} };
        await using var db=TestDb.CreateContext(_clock);var f=await Seed(db);var request=Request(f,f.ItemId);request.SupplierInvoiceNumber=number;request.ParsedTotal=expected;request.ReviewedTotal=expected;
        request.Lines=pairs.Select((pair,index)=>new PurchaseImageImportLine { ExistingItemId=f.ItemId,SourceName="Printed row "+index,ItemName="Reviewed medicine",CategoryId=f.CategoryId,BaseUnitOfMeasureId=f.UnitOfMeasureId,
            ReceiveUnitName="علبة",BaseUnitsPerReceiveUnit=20,Quantity=pair.Item1,BonusQuantity=pair.Item2,UnitCost=pair.Item3,BatchNumber="REVIEWED-"+index,ExpiryDate=new(2028,10,1),Reviewed=true }).ToList();
        var result=await Import(db).SaveReviewedAsync(request);result.Succeeded.Should().BeTrue(string.Join(";",result.Errors));
        (await Purchases(db).PostGoodsReceiptAsync(result.Value!.GoodsReceiptId,1)).Succeeded.Should().BeTrue();
        (await db.Batches.SumAsync(b=>b.QuantityOnHand)).Should().Be(pairs.Sum(pair=>(pair.Item1+pair.Item2)*20));
        var invoiceDto=(await Purchases(db).PrefillInvoiceFromGoodsReceiptAsync(result.Value.GoodsReceiptId))!;
        var invoice=await Purchases(db).CreatePurchaseInvoiceAsync(invoiceDto);invoice.Succeeded.Should().BeTrue();invoice.Value!.TotalAmount.Should().Be(expected);
    }

}
