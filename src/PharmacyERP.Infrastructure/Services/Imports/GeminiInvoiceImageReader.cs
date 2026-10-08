using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Purchasing.Imports;

namespace PharmacyERP.Infrastructure.Services.Imports;

public sealed class GeminiVisionOptions
{
    public Func<string?> ApiKey { get; init; } = () => Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
    public Func<string> Model { get; init; } = () => Environment.GetEnvironmentVariable("PHARMACYERP_GEMINI_MODEL") ?? "gemini-2.5-flash";
}

/// <summary>Gemini Developer API transcription only; all stock changes require review.</summary>
public sealed class GeminiInvoiceImageReader : IInvoiceImageReader
{
    private readonly HttpClient _http;
    private readonly GeminiVisionOptions _options;
    public GeminiInvoiceImageReader(HttpClient http, GeminiVisionOptions options) { _http = http; _options = options; }
    public string ProviderName => "Gemini";

    public async Task<Result<InvoiceImageDocument>> ReadAsync(InvoiceImageInput image, CancellationToken cancellationToken = default)
    {
        var error = InvoiceImageContract.Validate(image);
        if (error is not null) return Result<InvoiceImageDocument>.Failure(error);
        var key = _options.ApiKey();
        if (string.IsNullOrWhiteSpace(key))
            return Result<InvoiceImageDocument>.Failure("قراءة الصور عبر Gemini تحتاج مفتاح Google AI Studio. افتح إعدادات النظام ← قراءة الفواتير، وأدخل المفتاح واحفظه.");
        var model = _options.Model();
        if (!Regex.IsMatch(model, @"\Agemini-[a-zA-Z0-9._-]+\z"))
            return Result<InvoiceImageDocument>.Failure("اسم نموذج Gemini غير صالح؛ راجع PHARMACYERP_GEMINI_MODEL.");
        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = InvoiceImageContract.Instructions } } },
            contents = new[] { new { role = "user", parts = new object[] {
                new { text = InvoiceImageContract.UserPrompt },
                new { inlineData = new { mimeType = image.MimeType, data = Convert.ToBase64String(image.Content) } }
            } } },
            generationConfig = new
            {
                temperature = 0, maxOutputTokens = 12000,
                responseMimeType = "application/json", responseJsonSchema = InvoiceImageContract.Schema()
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        // Keep the key out of URLs and diagnostics. Never fall back to a paid provider.
        request.Headers.Add("x-goog-api-key", key.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Result<InvoiceImageDocument>.Failure(response.StatusCode switch
                {
                    System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "تعذر تفعيل قراءة Gemini؛ راجع المفتاح وصلاحياته وتوفر الخدمة في حساب Google AI Studio.",
                    System.Net.HttpStatusCode.NotFound => "نموذج Gemini غير متاح لحسابك؛ راجع PHARMACYERP_GEMINI_MODEL.",
                    System.Net.HttpStatusCode.TooManyRequests => "بلغت حد استخدام Gemini؛ انتظر ثم أعد المحاولة أو راجع حدود الخطة المجانية في Google AI Studio. لم يُضف مخزون.",
                    _ => $"تعذر تحليل الصورة عبر Gemini (HTTP {(int)response.StatusCode}). لم يُضف مخزون."
                });
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!payload.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return Result<InvoiceImageDocument>.Failure("لم يتمكن Gemini من قراءة المستند؛ جرّب صورة أوضح أو أدخل الفاتورة يدوياً.");
            var candidate = candidates[0];
            if (candidate.GetProperty("finishReason").GetString() != "STOP")
                return Result<InvoiceImageDocument>.Failure("قراءة Gemini غير مكتملة. جرّب صفحة واحدة أو صورة أقرب. لم يُضف مخزون.");
            var parts = candidate.GetProperty("content").GetProperty("parts");
            var json = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.GetBoolean()) continue;
                if (part.TryGetProperty("text", out var text)) json.Append(text.GetString());
                if (json.Length > 1024 * 1024) return Result<InvoiceImageDocument>.Failure("استجابة Gemini أكبر من الحد المسموح؛ حلل صفحة واحدة.");
            }
            return InvoiceImageContract.Parse(json.ToString(), image);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Result<InvoiceImageDocument>.Failure("انتهت مهلة قراءة Gemini؛ حاول مجدداً. لم يُضف مخزون."); }
        catch (HttpRequestException)
        { return Result<InvoiceImageDocument>.Failure("تعذر الاتصال بـ Gemini؛ راجع الإنترنت. لم يُضف مخزون."); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { return Result<InvoiceImageDocument>.Failure("استجابة Gemini غير مكتملة أو غير صالحة؛ أعد التحليل أو أدخل الفاتورة يدوياً."); }
    }
}
