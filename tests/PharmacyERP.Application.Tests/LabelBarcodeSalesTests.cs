using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using Xunit;

namespace PharmacyERP.Application.Tests;

public partial class PackagingSalesTests
{
    [Fact]
    public async Task MainUnitLabelWithFactorOne_DoesNotFollowTheSelectedLargerAdditionalUnit()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db, 1);
        (await db.Items.SingleAsync()).BaseUnitBarcode = null;
        db.ItemSaleUnits.Add(new ItemSaleUnit { ItemId = fixture.ItemId, Name = "علبة", BaseUnitCount = 5, Barcode = "BOX5" });
        await db.SaveChangesAsync(); await ReceiveAsync(db, fixture, boxes: 10, sale: 1250);
        var pos = await PosAsync(db, fixture);
        await pos.SubmitSearchAsync("BOX5", true);
        var box = pos.CartLines.Single(); box.UnitsPerSale.Should().Be(5);
        await pos.SubmitSearchAsync("1234567890", true);
        var piece = pos.CartLines.Single(l => l.UnitsPerSale == 1);
        piece.Quantity.Should().Be(1); piece.UnitPrice.Should().Be(1250);
        pos.SelectedCartLine = box;
        await pos.SubmitSearchAsync("1234567890", true);
        box.Quantity.Should().Be(1); piece.Quantity.Should().Be(2); pos.TotalAmount.Should().Be(8750);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(3);
    }

    [Fact]
    public async Task PackageLabelBarcode_AddsThePackageEvenWhenAStripWasSelectedLast()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db);
        await ReceiveAsync(db, fixture, boxes: 3);
        var pos = await PosAsync(db, fixture);
        await pos.SubmitSearchAsync("0987654321", true);
        var strip = pos.CartLines.Single(); strip.SellAsPackage.Should().BeFalse();
        await pos.SubmitSearchAsync("1234567890", true);
        pos.CartLines.Should().HaveCount(2);
        var box = pos.CartLines.Single(l => l.SellAsPackage);
        box.Quantity.Should().Be(1); box.UnitsPerSale.Should().Be(5); box.UnitPrice.Should().Be(10000);
        pos.SelectedCartLine = strip;
        await pos.SubmitSearchAsync("1234567890", true);
        box.Quantity.Should().Be(2); strip.Quantity.Should().Be(1); pos.TotalAmount.Should().Be(22000);
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().BeEmpty();
        (await db.Batches.SingleAsync()).QuantityOnHand.Should().Be(4);
        var saved = await db.SalesInvoiceItems.ToListAsync();
        saved.Single(l => l.UnitsPerSale == 5).Quantity.Should().Be(2);
        saved.Single(l => l.UnitsPerSale == 1).Quantity.Should().Be(1);
    }

    [Fact]
    public async Task PackageLabelBarcode_RefusesPartialStockWhileCatalogueCanStillSellTheSmallUnit()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db);
        await ReceiveAsync(db, fixture, boxes: 1);
        await Inventory(db).IssueStockFefoAsync(fixture.ItemId, fixture.WarehouseId, 2, "OtherSale", null, null);
        var pos = await PosAsync(db, fixture);
        await pos.SubmitSearchAsync("1234567890", true);
        pos.CartLines.Should().BeEmpty(); pos.ErrorMessage.Should().Contain("عبوة كاملة");
        pos.ProductCards.Single().UsePackage.Should().BeFalse();
        await pos.AddProductAsync(pos.ProductCards.Single());
        pos.ErrorMessage.Should().BeEmpty();
        var strip = pos.CartLines.Single(); strip.UnitsPerSale.Should().Be(1); strip.UnitPrice.Should().Be(2000);
        await pos.SubmitSearchAsync("1234567890", true);
        strip.Quantity.Should().Be(1); pos.ErrorMessage.Should().Contain("عبوة كاملة");
        await pos.SubmitSearchAsync("0987654321", true);
        strip.Quantity.Should().Be(2); pos.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task PackageAndAdditionalUnitLabels_KeepTheirOwnUnitsRegardlessOfSelection()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db, 100);
        (await db.UnitsOfMeasure.SingleAsync()).Name = "حبة";
        db.ItemSaleUnits.Add(new ItemSaleUnit { ItemId = fixture.ItemId, Name = "شريط", BaseUnitCount = 20, Barcode = "STRIP20" });
        await db.SaveChangesAsync(); await ReceiveAsync(db, fixture, boxes: 2, sale: 1250);
        var pos = await PosAsync(db, fixture);
        await pos.SubmitSearchAsync("STRIP20", true);
        var strip = pos.CartLines.Single(); strip.UnitsPerSale.Should().Be(20);
        await pos.SubmitSearchAsync("1234567890", true);
        var box = pos.CartLines.Single(l => l.SellAsPackage); box.UnitsPerSale.Should().Be(100);
        pos.SelectedCartLine = box;
        await pos.SubmitSearchAsync("STRIP20", true);
        strip.Quantity.Should().Be(2); box.Quantity.Should().Be(1);
        await pos.SubmitSearchAsync("0987654321", true);
        pos.CartLines.Single(l => l.UnitsPerSale == 1).Quantity.Should().Be(1);
        pos.TotalAmount.Should().Be(1762.50m);
    }
}
