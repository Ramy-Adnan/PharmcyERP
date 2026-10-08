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
    private readonly IInvoiceImageReader _openAi, _gemini, _local;
    private readonly IInvoiceAiSettingsStore? _settingsStore;

    public ConfiguredInvoiceImageReader(HttpClient http, InvoiceVisionOptions openAiOptions, GeminiVisionOptions geminiOptions,
        Func<string?>? provider = null, IInvoiceAiSettingsStore? settingsStore = null, ILocalInvoiceOcr? localOcr = null)
    {
        _http = http; _openAiOptions = openAiOptions; _geminiOptions = geminiOptions; _settingsStore = settingsStore;
        _provider = provider ?? (() => Environment.GetEnvironmentVariable("PHARMACYERP_VISION_PROVIDER"));
        _openAi = new OpenAiInvoiceImageReader(http, openAiOptions);
        _gemini = new GeminiInvoiceImageReader(http, geminiOptions);
        _local = new LocalOcrInvoiceImageReader(localOcr ?? new TesseractInvoiceOcr());
    }

    public bool ProcessesLocally => ProviderName == "LocalOCR";

    public string ProviderName
    {
        get
        {
            try { if (_settingsStore?.Load() is { } saved) return saved.Provider; }
            catch (InvalidOperationException) { return "خدمة الصور المحفوظة"; }
            return EnvironmentProviderName();
        }
    }

    private string EnvironmentProviderName()
    {
        var configured = _provider()?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.ToLowerInvariant() switch { "localocr" => "LocalOCR", "gemini" => "Gemini", "openai" => "OpenAI", _ => "غير محدد" };
        // Preserve installed OpenAI configurations; prefer Gemini when its key is present.
        if (!string.IsNullOrWhiteSpace(_geminiOptions.ApiKey())) return "Gemini";
        return !string.IsNullOrWhiteSpace(_openAiOptions.ApiKey()) ? "OpenAI" : "LocalOCR";
    }

    public Task<Result<InvoiceImageDocument>> ReadAsync(InvoiceImageInput image, CancellationToken cancellationToken = default)
    {
        try
        {
            // Snapshot once per request. Saving new settings takes effect on the next analysis.
            if (_settingsStore?.Load() is { } saved)
            {
                IInvoiceImageReader reader = saved.Provider == "LocalOCR" ? _local : saved.Provider == "Gemini"
                    ? new GeminiInvoiceImageReader(_http, new GeminiVisionOptions { ApiKey = () => saved.ApiKey, Model = () => saved.Model })
                    : new OpenAiInvoiceImageReader(_http, new InvoiceVisionOptions { ApiKey = () => saved.ApiKey, Model = () => saved.Model });
                return reader.ReadAsync(image, cancellationToken);
            }
        }
        catch (InvalidOperationException)
        { return Task.FromResult(Result<InvoiceImageDocument>.Failure("تعذر قراءة إعدادات خدمة الصور. افتح إعدادات النظام وأعد حفظ اختيار الخدمة. لم تُرسل الصورة.")); }
        return EnvironmentProviderName() switch
        {
            "LocalOCR" => _local.ReadAsync(image, cancellationToken),
            "Gemini" => _gemini.ReadAsync(image, cancellationToken),
            "OpenAI" => _openAi.ReadAsync(image, cancellationToken),
            _ => Task.FromResult(Result<InvoiceImageDocument>.Failure("قيمة PHARMACYERP_VISION_PROVIDER يجب أن تكون LocalOCR أو Gemini أو OpenAI. لم تُرسل الصورة."))
        };
    }

    public void Dispose() => _http.Dispose();
}
