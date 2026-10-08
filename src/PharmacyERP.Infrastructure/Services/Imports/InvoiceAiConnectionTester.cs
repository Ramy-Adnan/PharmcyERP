using System.Net;
using System.Net.Http.Headers;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Purchasing.Imports;

namespace PharmacyERP.Infrastructure.Services.Imports;

/// <summary>Checks credentials and model availability without uploading an invoice or generating tokens.</summary>
public sealed class InvoiceAiConnectionTester : IInvoiceAiConnectionTester, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILocalInvoiceOcr _localOcr;
    public InvoiceAiConnectionTester(HttpClient http, ILocalInvoiceOcr? localOcr = null) { _http = http; _localOcr = localOcr ?? new TesseractInvoiceOcr(); }
    public async Task<Result> TestAsync(InvoiceAiSettings settings, CancellationToken cancellationToken = default)
    {
        try { EncryptedInvoiceAiSettingsStore.Validate(settings); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }
        if (settings.Provider == "LocalOCR") return await _localOcr.CheckAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) return Result.Failure("أدخل مفتاح الخدمة أولاً.");
        var gemini = settings.Provider == "Gemini";
        using var request = new HttpRequestMessage(HttpMethod.Get, gemini
            ? $"https://generativelanguage.googleapis.com/v1beta/models/{settings.Model}"
            : $"https://api.openai.com/v1/models/{settings.Model}");
        if (gemini) request.Headers.Add("x-goog-api-key", settings.ApiKey.Trim());
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode ? Result.Success() : Result.Failure(response.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "المفتاح غير صالح أو الخدمة غير متاحة لحسابك؛ راجع مفتاح الخدمة وصلاحياته.",
                HttpStatusCode.NotFound => "النموذج غير متاح؛ راجع اسم النموذج.",
                HttpStatusCode.TooManyRequests => "بلغت حد الاستخدام؛ انتظر ثم أعد المحاولة.",
                _ => "تعذر الاتصال بخدمة الصور؛ حاول لاحقاً."
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Result.Failure("انتهت مهلة اختبار الاتصال؛ راجع الإنترنت."); }
        catch (HttpRequestException) { return Result.Failure("تعذر الاتصال؛ راجع الإنترنت."); }
    }
    public void Dispose() => _http.Dispose();
}
