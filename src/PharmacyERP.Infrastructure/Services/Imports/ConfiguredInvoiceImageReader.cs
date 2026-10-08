using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Purchasing.Imports;

namespace PharmacyERP.Infrastructure.Services.Imports;

/// <summary>Selects exactly one provider. Keys are never sent to a different provider.</summary>
public sealed class ConfiguredInvoiceImageReader : IInvoiceImageReader, IDisposable
{
    private readonly HttpClient _http;
    private readonly InvoiceVisionOptions _openAiOptions;
    private readonly GeminiVisionOptions _geminiOptions;
    private readonly Func<string?> _provider;
    private readonly IInvoiceImageReader _openAi, _gemini;

    public ConfiguredInvoiceImageReader(HttpClient http, InvoiceVisionOptions openAiOptions, GeminiVisionOptions geminiOptions,
        Func<string?>? provider = null)
    {
        _http = http; _openAiOptions = openAiOptions; _geminiOptions = geminiOptions;
        _provider = provider ?? (() => Environment.GetEnvironmentVariable("PHARMACYERP_VISION_PROVIDER"));
        _openAi = new OpenAiInvoiceImageReader(http, openAiOptions);
        _gemini = new GeminiInvoiceImageReader(http, geminiOptions);
    }

    public string ProviderName
    {
        get
        {
            var configured = _provider()?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.ToLowerInvariant() switch { "gemini" => "Gemini", "openai" => "OpenAI", _ => "غير محدد" };
            // Preserve installed OpenAI configurations; prefer Gemini when its key is present.
            if (!string.IsNullOrWhiteSpace(_geminiOptions.ApiKey())) return "Gemini";
            return !string.IsNullOrWhiteSpace(_openAiOptions.ApiKey()) ? "OpenAI" : "Gemini";
        }
    }

    public Task<Result<InvoiceImageDocument>> ReadAsync(InvoiceImageInput image, CancellationToken cancellationToken = default)
        => ProviderName switch
        {
            "Gemini" => _gemini.ReadAsync(image, cancellationToken),
            "OpenAI" => _openAi.ReadAsync(image, cancellationToken),
            _ => Task.FromResult(Result<InvoiceImageDocument>.Failure("قيمة PHARMACYERP_VISION_PROVIDER يجب أن تكون Gemini أو OpenAI. لم تُرسل الصورة."))
        };

    public void Dispose() => _http.Dispose();
}
