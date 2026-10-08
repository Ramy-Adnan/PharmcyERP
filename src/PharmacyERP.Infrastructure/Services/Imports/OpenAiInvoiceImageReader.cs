using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Purchasing.Imports;

namespace PharmacyERP.Infrastructure.Services.Imports;

public sealed class InvoiceVisionOptions
{
    public Func<string?> ApiKey { get; init; } = () => Environment.GetEnvironmentVariable("PHARMACYERP_VISION_API_KEY") ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    public Func<string> Model { get; init; } = () => Environment.GetEnvironmentVariable("PHARMACYERP_VISION_MODEL") ?? "gpt-4.1-mini";
}
/// <summary>Structured vision extraction only. The review/import service owns all database changes.</summary>
public sealed class OpenAiInvoiceImageReader : IInvoiceImageReader
{
    private readonly HttpClient _http;
    private readonly InvoiceVisionOptions _options;
    public OpenAiInvoiceImageReader(HttpClient http, InvoiceVisionOptions options) { _http = http; _options = options; }
    public const int MaxImageBytes = InvoiceImageContract.MaxImageBytes;
    public string ProviderName => "OpenAI";
    public async Task<Result<InvoiceImageDocument>> ReadAsync(InvoiceImageInput image, CancellationToken cancellationToken = default)
    {
        var error = InvoiceImageContract.Validate(image);
        if (error is not null) return Result<InvoiceImageDocument>.Failure(error);
        var bytes = image.Content;
        var key = _options.ApiKey();
        if (string.IsNullOrWhiteSpace(key)) return Result<InvoiceImageDocument>.Failure("قراءة الصور تحتاج مفتاح OpenAI API. افتح إعدادات النظام ← قراءة الفواتير، وأدخل المفتاح واحفظه.");
        var requestBody = new
        {
            model = _options.Model(), temperature = 0, max_completion_tokens = 12000,
            messages = new object[]
            {
                new { role = "system", content = InvoiceImageContract.Instructions },
                new { role = "user", content = new object[] { new { type = "text", text = InvoiceImageContract.UserPrompt },
                    new { type = "image_url", image_url = new { url = $"data:{image.MimeType};base64,{Convert.ToBase64String(bytes)}", detail = "high" } } } }
            },
            response_format = new { type = "json_schema", json_schema = new { name = "pharmacy_supplier_invoice", strict = true, schema = InvoiceImageContract.Schema() } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Result<InvoiceImageDocument>.Failure(response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "مفتاح خدمة تحليل الصور غير صالح؛ راجع إعداداته.",
                    System.Net.HttpStatusCode.TooManyRequests => "خدمة تحليل الصور وصلت حد الاستخدام أو الرصيد؛ حاول لاحقاً أو راجع حساب API.",
                    _ => $"تعذر تحليل الصورة (HTTP {(int)response.StatusCode}). لم يُضف أي مخزون."
                });
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var choice = payload.RootElement.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop") return Result<InvoiceImageDocument>.Failure("القراءة غير مكتملة. جرّب صورة أقرب أو صفحة واحدة.");
            var message = choice.GetProperty("message");
            if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String)
                return Result<InvoiceImageDocument>.Failure("لم تتمكن خدمة الصور من قراءة المستند؛ جرّب صورة أوضح.");
            var json = message.GetProperty("content").GetString();
            return InvoiceImageContract.Parse(json, image);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Result<InvoiceImageDocument>.Failure("انتهت مهلة قراءة الصورة؛ حاول مجدداً. لم يُضف مخزون."); }
        catch (HttpRequestException) { return Result<InvoiceImageDocument>.Failure("تعذر الاتصال بخدمة الصور؛ راجع الإنترنت. لم يُضف مخزون."); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { return Result<InvoiceImageDocument>.Failure("استجابة القراءة غير مكتملة أو غير صالحة؛ أعد التحليل أو أدخل الفاتورة يدوياً."); }
    }
}
