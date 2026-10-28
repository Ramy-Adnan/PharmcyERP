using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Entities;
using Xunit;

namespace PharmacyERP.Application.Tests;

public partial class PackagingSalesTests
{
    [Fact]
    public async Task BasketPresentationAndDiscountEdits_DoNotRequoteBatchPrices()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db, 1);
        await ReceiveAsync(db, fixture, sale: 1250);
        var pos = await PosAsync(db, fixture); await pos.SubmitSearchAsync("1234567890", true);
        var row = pos.CartLines.Single(); var quotes = 0;
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(row.PriceBreakdown)) quotes++; };
        row.PhotoPath = "preview.png";
        row.DiscountAmount = 50;
        pos.DiscountAmount = 25;
        quotes.Should().Be(0, "pictures and discounts do not affect which batch supplies a unit");
        pos.TotalAmount.Should().Be(1175);
        row.Quantity = 2;
        quotes.Should().Be(1); pos.TotalAmount.Should().Be(2425);
        row.UnitPrice = 1500;
        row.UsesBatchPricing.Should().BeFalse(); pos.TotalAmount.Should().Be(2925);
    }

    [Fact]
    public async Task StockRefresh_UpdatesCardsAndAvailability_PreservesSearchAndExplicitManualPrice()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db, 1);
        await ReceiveAsync(db, fixture, sale: 1250);
        var pos = await PosAsync(db, fixture); await pos.SubmitSearchAsync("1234567890", true);
        var row = pos.CartLines.Single(); row.UnitPrice = 1700;
        var batch = await db.Batches.SingleAsync(); batch.PackageSalePrice = 2500; batch.SalePriceOverride = 2500;
        await db.SaveChangesAsync();
        await Inventory(db).IssueStockFefoAsync(fixture.ItemId, fixture.WarehouseId, 1, "OtherRegister", null, null);
        pos.SearchText = "1234567890";
        await pos.RefreshStockAsync();
        pos.ErrorMessage.Should().BeEmpty(); pos.SearchText.Should().Be("1234567890");
        pos.ProductCards.Single().Price.Should().Be(2500);
        row.AvailableBaseQuantity.Should().Be(1); row.UnitPrice.Should().Be(1700); row.UsesBatchPricing.Should().BeFalse();
        row.UseAutomaticPricing(); row.UnitPrice.Should().Be(2500);
        (await db.Items.SingleAsync()).IsActive = false; await db.SaveChangesAsync();
        await pos.RefreshStockAsync();
        row.AvailableBaseQuantity.Should().Be(0); pos.ProductCards.Should().BeEmpty();
        await pos.CheckoutAsync(); pos.ErrorMessage.Should().NotBeEmpty();
        (await db.SalesInvoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task QuantityEdit_OnlyRepricesRowsSharingTheSameItem()
    {
        await using var db = TestDb.CreateContext(_clock); var fixture = await SeedAsync(db, 1);
        await ReceiveAsync(db, fixture, sale: 1250);
        var other = new Item { Code = "OTHER", Name = "دواء آخر", Barcode = "111222333",
            CategoryId = fixture.CategoryId, UnitOfMeasureId = fixture.UnitOfMeasureId,
            UnitsPerPackage = 1, DefaultPurchasePrice = 1000, DefaultSalePrice = 1250 };
        db.Items.Add(other); await db.SaveChangesAsync();
        (await Inventory(db).ReceiveBatchAsync(new() { ItemId = other.Id, WarehouseId = fixture.WarehouseId,
            BatchNumber = "OTHER-BATCH", Quantity = 2, PurchasePrice = 1000, ExpiryDate = _clock.UtcNow.AddDays(100) }, 1)).Succeeded.Should().BeTrue();
        var pos = await PosAsync(db, fixture); await pos.SubmitSearchAsync("1234567890", true); await pos.SubmitSearchAsync("111222333", true);
        var first = pos.CartLines.Single(l => l.ItemId == fixture.ItemId);
        var second = pos.CartLines.Single(l => l.ItemId == other.Id); var otherQuotes = 0;
        second.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(second.PriceBreakdown)) otherQuotes++; };
        first.Quantity = 2;
        otherQuotes.Should().Be(0); pos.TotalAmount.Should().Be(3750);
    }
}
