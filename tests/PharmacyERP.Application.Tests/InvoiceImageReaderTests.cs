using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Inventory;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Infrastructure.Services;
using PharmacyERP.Infrastructure.Services.Imports;
using PharmacyERP.WPF.ViewModels.Purchasing;
using Xunit;
namespace PharmacyERP.Application.Tests;
public class InvoiceImageReaderTests
{
    private static InvoiceImageInput Image() => new("invoice15275.jpg","image/jpeg",new byte[]{255,216,255,224,1,2,3});
    private const string DocumentJson="""
        {"supplier_name":"مذخر الإسراء","invoice_number":"15275","invoice_date":"2026-10-07","invoice_total":39000,"currency":"IQD","notes":"Batch not visible; 20 tablets is not a strip count","lines":[{"name":"Paracetamol 1g 20 Tab Cisen Qidu","barcode":null,"quantity":10,"bonus_quantity":0,"unit_price":3900,"line_total":39000,"batch_number":null,"expiry_date":"2028-04-01","declared_unit_count":20,"declared_unit_kind":"tablet","strength":"1g","notes":"Batch blank"}]}
        """;
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;public string? Body;public Uri? Uri;public string? Auth;public HttpStatusCode Status=HttpStatusCode.OK;public string Json=DocumentJson;public string Finish="stop";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;Uri=request.RequestUri;Auth=request.Headers.Authorization?.ToString();Body=await request.Content!.ReadAsStringAsync(token);
            return new(Status){Content=new StringContent(JsonSerializer.Serialize(new{choices=new[]{new{finish_reason=Finish,message=new{content=Json}}}}))};
        }
    }
    private static OpenAiInvoiceImageReader Reader(Handler handler,string? key="test-only-key") => new(new HttpClient(handler),new InvoiceVisionOptions{ApiKey=()=>key,Model=()=>"gpt-4.1-mini"});
    [Fact]
    public async Task StructuredVisionContract_UsesOfficialEndpointAndImage_AndPreservesUnknownBatch()
    {
        var handler=new Handler();var input=Image();var result=await Reader(handler).ReadAsync(input);result.Succeeded.Should().BeTrue(string.Join(";",result.Errors));
        handler.Uri!.AbsoluteUri.Should().Be("https://api.openai.com/v1/chat/completions");handler.Auth.Should().Be("Bearer test-only-key");
        using var request=JsonDocument.Parse(handler.Body!);request.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean().Should().BeTrue();
        var data=request.RootElement.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString();data.Should().Be("data:image/jpeg;base64,"+Convert.ToBase64String(input.Content));
        result.Value!.SourceHash.Should().Be(Convert.ToHexString(SHA256.HashData(input.Content)));var row=result.Value.Lines.Single();
        row.UnitPrice.Should().Be(3900);row.Quantity.Should().Be(10);row.DeclaredUnitCount.Should().Be(20);row.BatchNumber.Should().BeNull();row.Barcode.Should().BeNull();row.ExpiryDate.Should().Be(new DateTime(2028,4,1));
    }
    [Fact]
    public async Task MissingKey_DoesNotSendTheImage()
    {var handler=new Handler();var result=await Reader(handler,null).ReadAsync(Image());result.Succeeded.Should().BeFalse();result.Errors.Single().Should().Contain("PHARMACYERP_VISION_API_KEY");handler.Calls.Should().Be(0);}
    [Theory]
    [InlineData(401)][InlineData(429)][InlineData(500)]
    public async Task ServiceErrorsAreClear_WithoutReturningTheSecret(int status)
    {var handler=new Handler{Status=(HttpStatusCode)status};var result=await Reader(handler).ReadAsync(Image());result.Succeeded.Should().BeFalse();string.Join(";",result.Errors).Should().NotContain("test-only-key");}
    [Theory]
    [InlineData("bad")][InlineData("truncated")][InlineData("empty")]
    public async Task MalformedOrIncompleteTranscription_IsNeverAccepted(string problem)
    {
        var handler=new Handler();if(problem=="bad")handler.Json="broken";if(problem=="truncated")handler.Finish="length";if(problem=="empty")handler.Json=DocumentJson.Replace("[{\"name\"", "[{\"unknown_name\"");
        (await Reader(handler).ReadAsync(Image())).Succeeded.Should().BeFalse();
    }
    [Fact]
    public async Task InvalidImageSignature_IsRejectedBeforeNetwork()
    {var handler=new Handler();(await Reader(handler).ReadAsync(new("fake.jpg","image/jpeg",new byte[]{1,2,3}))).Succeeded.Should().BeFalse();handler.Calls.Should().Be(0);}
    [Fact]
    public async Task ReviewedScreen_UsesTabletsFor20Tab_AndRequiresAManualBatchBeforeSaving()
    {
        var clock=new FakeDateTime {UtcNow=new(2026,10,8)};await using var db=TestDb.CreateContext(clock);var f=await TestDb.SeedBaselineAsync(db);
        (await db.UnitsOfMeasure.FindAsync(f.UnitOfMeasureId))!.Name="حبة";await db.SaveChangesAsync();
        var inventory=new InventoryService(db,clock);var purchasing=new PurchasingService(db,inventory,new AccountingService(db,clock),clock);
        var vm=new InvoiceImageImportViewModel(Reader(new Handler()),new PurchaseImageImportService(db,inventory,purchasing,new FakeCurrentUserService(),clock),inventory);
        await vm.InitializeAsync(1,f.BranchId,f.WarehouseId);await vm.AnalyzeAsync(Image());
        var row=vm.Lines.Single();row.BaseUnitOfMeasureId.Should().Be(f.UnitOfMeasureId);row.BaseUnitsPerReceiveUnit.Should().Be(20);row.BasePurchasePrice.Should().Be(195);row.Reviewed.Should().BeFalse();row.BatchNumber.Should().BeEmpty();
        (await vm.SaveAsync()).Should().BeFalse();(await db.GoodsReceiptNotes.CountAsync()).Should().Be(0);
    }
}
