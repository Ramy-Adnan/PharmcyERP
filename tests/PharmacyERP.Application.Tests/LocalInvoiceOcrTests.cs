using System.Net;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.Infrastructure.Identity;
using PharmacyERP.Infrastructure.Services.Imports;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.WPF.ViewModels.Purchasing;
using PharmacyERP.WPF.ViewModels.SystemManagement;
using Xunit;

namespace PharmacyERP.Application.Tests;

public sealed class NativeOcrFactAttribute : FactAttribute
{
    public NativeOcrFactAttribute()
    {
        if (OperatingSystem.IsWindows()) return;
        // The distributed Windows DLLs run on client PCs. Linux integration uses ABI-compatible distro libraries.
        if (!OperatingSystem.IsLinux() || !File.Exists("/lib/x86_64-linux-gnu/libtesseract.so.5") || !File.Exists("/lib/x86_64-linux-gnu/libleptonica.so.6"))
            Skip = "Native OCR integration needs Windows VC++ runtime, or Linux libtesseract5 and libleptonica6.";
    }
}

public class LocalInvoiceOcrTests
{
    private static InvoiceImageInput Image() => new("invoice.jpg", "image/jpeg", new byte[] { 255, 216, 255, 1 });
    private static InvoiceOcrWord Word(string text, int x, int y, float confidence = 95, int width = 65) => new(text, x, y, width, 20, confidence);
    private static InvoiceOcrPage Table(bool arabic = false, bool lowConfidence = false, string name = "Paracetamol 1g 20 Tab")
    {
        var words = new List<InvoiceOcrWord>
        {
            Word("Invoice No: 15275", 10, 10, width: 200), Word("Date: 07-10-2026", 400, 10, width: 200),
            Word(arabic ? "الصلاحية" : "Expiry", 10, 70), Word(arabic ? "الطلبية" : "Batch", 110, 70),
            Word(arabic ? "الاجمالي" : "Amount", 220, 70), Word(arabic ? "المفرد" : "Price", 340, 70),
            Word(arabic ? "البونص" : "Bonus", 460, 70), Word(arabic ? "الكمية" : "Qty", 560, 70), Word(arabic ? "المادة" : "Description", 700, 70),
            Word("01-04-2028", 10, 115, width: 90), Word("B123", 110, 115), Word("39,000", 220, 115), Word("3,900", 340, 115),
            Word("2", 460, 115), Word("10", 560, 115, lowConfidence ? 20 : 95), Word(name, 680, 115, width: 240),
            Word("Grand Total:", 10, 190, width: 140), Word("39,000", 240, 190), Word("Supplier Balance:", 10, 230, width: 190), Word("631,649", 240, 230)
        };
        return new("Invoice No: 15275 Date: 07-10-2026 Currency IQD\n" + name, words);
    }
    private sealed class Ocr : ILocalInvoiceOcr
    {
        public InvoiceOcrPage Page { get; init; } = Table();
        public int Reads { get; private set; }
        public int Checks { get; private set; }
        public Task<InvoiceOcrPage> ReadAsync(byte[] image, CancellationToken token) { token.ThrowIfCancellationRequested(); Reads++; return Task.FromResult(Page); }
        public Task<Result> CheckAsync(CancellationToken token) { Checks++; return Task.FromResult(Result.Success()); }
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
    }
    private sealed class Store : IInvoiceAiSettingsStore
    {
        public InvoiceAiSettings? Saved { get; set; }
        public InvoiceAiSettings? Load() => Saved;
        public void Save(InvoiceAiSettings settings) => Saved = settings;
    }
    private static InvoiceAiSettings Local() => new() { Provider = "LocalOCR", Model = "tesseract-ara-eng" };

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GeometryParser_UsesRecognizedHeaders_SeparatesBonusAndBalance_AndPreservesPackagingMeaning(bool arabic)
    {
        var result = LocalInvoiceTableParser.Parse(Table(arabic)); var line = result.Lines.Single();
        result.InvoiceNumber.Should().Be("15275"); result.InvoiceDate.Should().Be(new DateTime(2026, 10, 7));
        result.InvoiceTotal.Should().Be(39000); result.Currency.Should().Be("IQD");
        line.Quantity.Should().Be(10); line.BonusQuantity.Should().Be(2); line.UnitPrice.Should().Be(3900); line.LineTotal.Should().Be(39000);
        line.BatchNumber.Should().Be("B123"); line.ExpiryDate.Should().Be(new DateTime(2028, 4, 1));
        line.DeclaredUnitCount.Should().Be(20); line.DeclaredUnitKind.Should().Be("tablet"); line.Barcode.Should().BeNull();
    }
    [Fact]
    public void LowConfidenceQuantity_IsNotSilentlyReplacedByADerivedQuantity()
    { LocalInvoiceTableParser.Parse(Table(lowConfidence: true)).Lines.Single().Quantity.Should().BeNull(); }
    [Fact]
    public void SyrupVolume_DoesNotBecomeASellableUnitCount()
    { LocalInvoiceTableParser.Parse(Table(name: "Cold Syrup 125ml")).Lines.Single().DeclaredUnitCount.Should().BeNull(); }
    [Fact]
    public void UnknownTableLayout_ReturnsRawTextForManualReview_WithoutInventingRows()
    {
        var page = new InvoiceOcrPage("Paracetamol blurry invoice", new[] { Word("Paracetamol", 10, 10) });
        var result = LocalInvoiceTableParser.Parse(page); result.Lines.Should().BeEmpty(); result.RawOcrText.Should().Be(page.Text);
        result.InvoiceTotal.Should().BeNull(); result.Notes.Should().Contain("يدوياً");
    }
    [Fact]
    public void MissingBatchAndExpiry_StayMissing()
    {
        var page = Table(); page = page with { Words = page.Words.Where(w => w.Text is not ("B123" or "01-04-2028")).ToArray() };
        var line = LocalInvoiceTableParser.Parse(page).Lines.Single(); line.BatchNumber.Should().BeNull(); line.ExpiryDate.Should().BeNull();
    }
    [Fact]
    public async Task LocalReader_PreservesImageHashAndRawText_WithoutAnApiKey()
    {
        var result = await new LocalOcrInvoiceImageReader(new Ocr()).ReadAsync(Image());
        result.Succeeded.Should().BeTrue(); result.Value!.SourceHash.Should().Be(Convert.ToHexString(SHA256.HashData(Image().Content)));
        result.Value.RawOcrText.Should().NotBeNullOrWhiteSpace();
    }
    [Fact]
    public async Task SelectingLocalProvider_IgnoresCloudKeysAndDoesNotMakeHttpRequests()
    {
        var http = new NoNetwork(); var ocr = new Ocr(); var store = new Store { Saved = Local() };
        using var reader = new ConfiguredInvoiceImageReader(new HttpClient(http), new InvoiceVisionOptions { ApiKey = () => "paid-key" },
            new GeminiVisionOptions { ApiKey = () => "paid-google-key" }, () => "OpenAI", store, ocr);
        reader.ProcessesLocally.Should().BeTrue(); (await reader.ReadAsync(Image())).Succeeded.Should().BeTrue();
        ocr.Reads.Should().Be(1); http.Calls.Should().Be(0);
        using var tester = new InvoiceAiConnectionTester(new HttpClient(http), ocr);
        (await tester.TestAsync(Local())).Succeeded.Should().BeTrue(); ocr.Checks.Should().Be(1); http.Calls.Should().Be(0);
    }
    [Fact]
    public async Task LocalSettings_SaveAndTestWithoutAnyKey()
    {
        var store = new Store(); var http = new NoNetwork(); var ocr = new Ocr(); var user = new CurrentUserService();
        user.SetSession(1, "manager", 1, new[] { "Branches.Manage" });
        using var tester = new InvoiceAiConnectionTester(new HttpClient(http), ocr);
        var vm = new InvoiceAiSettingsViewModel(store, tester, user); vm.Initialize();
        vm.IsLocalOcr.Should().BeTrue(); vm.UsesApi.Should().BeFalse(); await vm.TestAsync(); vm.Save();
        store.Saved!.Provider.Should().Be("LocalOCR"); store.Saved.ApiKey.Should().BeEmpty(); http.Calls.Should().Be(0);
    }
    [Fact]
    public async Task InvalidImageIsRejectedBeforeOcr_AndCancellationPropagates()
    {
        var ocr = new Ocr(); var reader = new LocalOcrInvoiceImageReader(ocr);
        (await reader.ReadAsync(new("bad.jpg", "image/jpeg", new byte[] { 1, 2 }))).Succeeded.Should().BeFalse(); ocr.Reads.Should().Be(0);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Func<Task> read = () => reader.ReadAsync(Image(), cancelled.Token); await read.Should().ThrowAsync<OperationCanceledException>();
    }
    [NativeOcrFact]
    public async Task BundledLanguagesAndRealEngine_ReadARenderedInvoiceTable()
    {
        if (OperatingSystem.IsLinux())
        {
            NativeLibrary.SetDllImportResolver(typeof(Tesseract.TesseractEngine).Assembly,
                (name, _, _) => name == "libdl" ? NativeLibrary.Load("libdl.so.2") : IntPtr.Zero);
            var directory = Path.Combine(AppContext.BaseDirectory, "x64"); Directory.CreateDirectory(directory);
            foreach (var (name, target) in new[] { ("libtesseract50.so", "/lib/x86_64-linux-gnu/libtesseract.so.5"), ("libleptonica-1.82.0.so", "/lib/x86_64-linux-gnu/libleptonica.so.6") })
                if (!File.Exists(Path.Combine(directory, name))) File.CreateSymbolicLink(Path.Combine(directory, name), target);
        }
        var ocr = new TesseractInvoiceOcr(); var check = await ocr.CheckAsync(default); check.Succeeded.Should().BeTrue(string.Join(";", check.Errors));
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "local-ocr-invoice.png"));
        var result = await new LocalOcrInvoiceImageReader(ocr).ReadAsync(new("invoice.png", "image/png", bytes));
        result.Succeeded.Should().BeTrue(string.Join(";", result.Errors));
        result.Value!.Lines.Should().HaveCount(2, result.Value.RawOcrText);
        var tablet = result.Value.Lines[0]; tablet.Name.Should().Contain("Paracetamol"); tablet.Quantity.Should().Be(10);
        tablet.BonusQuantity.Should().Be(2); tablet.UnitPrice.Should().Be(3900); tablet.DeclaredUnitCount.Should().Be(20);
        result.Value.InvoiceTotal.Should().Be(49000); result.Value.RawOcrText.Should().Contain("Supplier");
    }

    [Fact]
    public async Task OcrReview_Calculates195PerTablet_ButDoesNotCreateStockWithoutApproval()
    {
        var clock = new FakeDateTime(); await using var db = TestDb.CreateContext(clock); var baseline = await TestDb.SeedBaselineAsync(db);
        (await db.UnitsOfMeasure.FindAsync(baseline.UnitOfMeasureId))!.Name = "حبة"; await db.SaveChangesAsync();
        var inventory = new InventoryService(db, clock); var purchasing = new PurchasingService(db, inventory, new AccountingService(db, clock), clock);
        var review = new InvoiceImageImportViewModel(new LocalOcrInvoiceImageReader(new Ocr()),
            new PurchaseImageImportService(db, inventory, purchasing, new FakeCurrentUserService(), clock), inventory);
        await review.InitializeAsync(1, baseline.BranchId, baseline.WarehouseId); await review.AnalyzeAsync(Image());
        review.HasRecognizedText.Should().BeTrue(); var line = review.Lines.Single(); line.BasePurchasePrice.Should().Be(195);
        line.Reviewed.Should().BeFalse(); (await review.SaveAsync()).Should().BeFalse();
        (await db.GoodsReceiptNotes.CountAsync()).Should().Be(0); (await db.Items.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UnknownLayout_ExposesTextAndAllowsAddingAndRemovingManualReviewLines()
    {
        var clock = new FakeDateTime(); await using var db = TestDb.CreateContext(clock); var baseline = await TestDb.SeedBaselineAsync(db);
        var inventory = new InventoryService(db, clock); var purchasing = new PurchasingService(db, inventory, new AccountingService(db, clock), clock);
        var review = new InvoiceImageImportViewModel(new LocalOcrInvoiceImageReader(new Ocr { Page = new("Unrecognized invoice text", Array.Empty<InvoiceOcrWord>()) }),
            new PurchaseImageImportService(db, inventory, purchasing, new FakeCurrentUserService(), clock), inventory);
        await review.InitializeAsync(1, baseline.BranchId, baseline.WarehouseId); await review.AnalyzeAsync(Image());
        review.HasRecognizedText.Should().BeTrue(); review.Lines.Should().BeEmpty();
        review.AddManualLine(); var line = review.Lines.Single(); line.Quantity.Should().BeNull(); line.ExpiryDate.Should().BeNull(); line.Reviewed.Should().BeFalse();
        line.Quantity = 2; line.UnitCost = 1000; review.ComputedTotal.Should().Be(2000);
        review.RemoveReviewLine(line); review.ComputedTotal.Should().Be(0); (await db.GoodsReceiptNotes.CountAsync()).Should().Be(0);
    }
}
