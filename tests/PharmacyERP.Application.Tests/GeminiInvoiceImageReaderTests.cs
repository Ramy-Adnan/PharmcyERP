using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using PharmacyERP.Application.Features.Purchasing.Imports;
using PharmacyERP.Infrastructure.Services.Imports;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class GeminiInvoiceImageReaderTests
{
    private static InvoiceImageInput Image() => new("invoice.jpg", "image/jpeg", new byte[] { 255, 216, 255, 224, 1, 2, 3 });
    private const string Document = """
        {"supplier_name":"مذخر الإسراء","invoice_number":"15275","invoice_date":"2026-10-07","invoice_total":39000,"currency":"IQD","notes":null,"lines":[{"name":"Paracetamol 1g 20 Tab","barcode":null,"quantity":10,"bonus_quantity":2,"unit_price":3900,"line_total":39000,"batch_number":null,"expiry_date":"2028-04-01","declared_unit_count":20,"declared_unit_kind":"tablet","strength":"1g","notes":"Batch not visible"}]}
        """;

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Key { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string Json { get; init; } = Document;
        public string Finish { get; init; } = "STOP";
        public string? Response { get; init; }
        public Exception? Failure { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Uri = request.RequestUri;
            Key = request.Headers.TryGetValues("x-goog-api-key", out var keys) ? keys.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(token);
            if (Failure is not null) throw Failure;
            var content = Uri!.Host == "api.openai.com"
                ? JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content = Json } } } })
                : JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = Finish, content = new { parts = new[] { new { text = Json } } } } } });
            return new(Status) { Content = new StringContent(Response ?? content) };
        }
    }

    private static GeminiVisionOptions Gemini(string? key = "gemini-test-key", string model = "gemini-2.5-flash")
        => new() { ApiKey = () => key, Model = () => model };
    private static GeminiInvoiceImageReader Reader(Handler handler, string? key = "gemini-test-key", string model = "gemini-2.5-flash")
        => new(new HttpClient(handler), Gemini(key, model));
    private static ConfiguredInvoiceImageReader Configured(Handler handler, string? provider, string? geminiKey = "gemini-test-key", string? openAiKey = "openai-test-key")
        => new(new HttpClient(handler), new InvoiceVisionOptions { ApiKey = () => openAiKey, Model = () => "gpt-4.1-mini" }, Gemini(geminiKey), () => provider);

    [Fact]
    public async Task NativeGeminiContract_PreservesPriceBonusAndUnknownBatch_AndKeepsKeyOutOfUrl()
    {
        var handler = new Handler(); var input = Image();
        var result = await Reader(handler).ReadAsync(input);
        result.Succeeded.Should().BeTrue(string.Join(";", result.Errors));
        handler.Uri!.AbsoluteUri.Should().Be("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");
        handler.Key.Should().Be("gemini-test-key"); handler.Authorization.Should().BeNull();
        using var body = JsonDocument.Parse(handler.Body!);
        var config = body.RootElement.GetProperty("generationConfig");
        config.GetProperty("responseMimeType").GetString().Should().Be("application/json");
        config.GetProperty("responseJsonSchema").GetProperty("properties").GetProperty("lines").GetProperty("items")
            .GetProperty("properties").GetProperty("batch_number").GetProperty("type")[1].GetString().Should().Be("null");
        var image = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData");
        image.GetProperty("mimeType").GetString().Should().Be("image/jpeg");
        image.GetProperty("data").GetString().Should().Be(Convert.ToBase64String(input.Content));
        handler.Body.Should().NotContain("gemini-test-key");
        result.Value!.SourceHash.Should().Be(Convert.ToHexString(SHA256.HashData(input.Content)));
        var line = result.Value.Lines.Single();
        line.UnitPrice.Should().Be(3900); line.Quantity.Should().Be(10); line.BonusQuantity.Should().Be(2);
        line.DeclaredUnitCount.Should().Be(20); line.BatchNumber.Should().BeNull(); line.Barcode.Should().BeNull();
        line.ExpiryDate.Should().Be(new DateTime(2028, 4, 1));
    }

    [Fact]
    public async Task MissingGeminiKey_DoesNotUseAnInstalledOpenAiKeyOrSendImage()
    {
        var handler = new Handler(); using var reader = Configured(handler, "Gemini", geminiKey: null);
        var result = await reader.ReadAsync(Image());
        reader.ProviderName.Should().Be("Gemini"); result.Succeeded.Should().BeFalse();
        result.Errors.Single().Should().Contain("إعدادات النظام"); handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("Gemini", "gemini-test-key", "openai-test-key", "Gemini", "generativelanguage.googleapis.com")]
    [InlineData("OpenAI", "gemini-test-key", "openai-test-key", "OpenAI", "api.openai.com")]
    [InlineData(null, "gemini-test-key", "openai-test-key", "Gemini", "generativelanguage.googleapis.com")]
    [InlineData(null, null, "openai-test-key", "OpenAI", "api.openai.com")]
    public async Task ProviderSelection_SendsOnlyItsOwnKey(string? provider, string? geminiKey, string? openAiKey, string name, string host)
    {
        var handler = new Handler(); using var reader = Configured(handler, provider, geminiKey, openAiKey);
        reader.ProviderName.Should().Be(name); (await reader.ReadAsync(Image())).Succeeded.Should().BeTrue();
        handler.Uri!.Host.Should().Be(host);
        if (name == "Gemini") { handler.Key.Should().Be(geminiKey); handler.Authorization.Should().BeNull(); }
        else { handler.Key.Should().BeNull(); handler.Authorization.Should().Be("Bearer " + openAiKey); }
    }

    [Fact]
    public async Task FreeQuotaExceeded_DoesNotRetryWithPaidProvider()
    {
        var handler = new Handler { Status = HttpStatusCode.TooManyRequests, Response = "gemini-test-key" };
        using var reader = Configured(handler, "Gemini");
        var result = await reader.ReadAsync(Image());
        result.Succeeded.Should().BeFalse(); result.Errors.Single().Should().Contain("حد استخدام Gemini").And.NotContain("gemini-test-key");
        handler.Calls.Should().Be(1); handler.Uri!.Host.Should().Be("generativelanguage.googleapis.com");
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(500)]
    public async Task ApiErrors_DoNotExposeKey(int status)
    {
        var result = await Reader(new Handler { Status = (HttpStatusCode)status, Response = "gemini-test-key" }).ReadAsync(Image());
        result.Succeeded.Should().BeFalse(); result.Errors.Single().Should().NotContain("gemini-test-key");
    }

    [Theory]
    [InlineData("malformed")] [InlineData("truncated")] [InlineData("blocked")] [InlineData("unknown-field")] [InlineData("empty")] [InlineData("invalid-date")]
    public async Task IncompleteOrMalformedReadings_AreRejected(string problem)
    {
        var handler = problem switch
        {
            "malformed" => new Handler { Response = "broken json" },
            "truncated" => new Handler { Finish = "MAX_TOKENS" },
            "blocked" => new Handler { Response = "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}" },
            "unknown-field" => new Handler { Json = Document.Replace("\"name\"", "\"unknown_name\"") },
            "empty" => new Handler { Json = "{\"lines\":[]}" },
            _ => new Handler { Json = Document.Replace("2028-04-01", "unknown") }
        };
        (await Reader(handler).ReadAsync(Image())).Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("bad-image")] [InlineData("oversize")] [InlineData("model-url")]
    public async Task InvalidInput_IsRejectedBeforeNetwork(string problem)
    {
        var handler = new Handler(); var input = Image();
        if (problem == "bad-image") input = input with { Content = new byte[] { 1, 2, 3 } };
        if (problem == "oversize") input = input with { Content = new byte[8 * 1024 * 1024 + 1] };
        var result = await Reader(handler, model: problem == "model-url" ? "gemini-test/../../other?key=secret" : "gemini-2.5-flash").ReadAsync(input);
        result.Succeeded.Should().BeFalse(); handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task UnsupportedProvider_DoesNotTransmitPhoto()
    {
        var handler = new Handler(); using var reader = Configured(handler, "unknown");
        (await reader.ReadAsync(Image())).Succeeded.Should().BeFalse(); handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Timeout_ReturnsAnActionableError_ButUserCancellationPropagates()
    {
        var handler = new Handler { Failure = new OperationCanceledException() };
        (await Reader(handler).ReadAsync(Image())).Errors.Single().Should().Contain("مهلة");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Func<Task> reading = () => Reader(handler).ReadAsync(Image(), cancelled.Token);
        await reading.Should().ThrowAsync<OperationCanceledException>();
    }
}
