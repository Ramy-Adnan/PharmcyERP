using PharmacyERP.Application.Common.Models;

namespace PharmacyERP.Application.Features.Purchasing.Imports;

public sealed class InvoiceAiSettings
{
    public string Provider { get; set; } = "Gemini";
    public string Model { get; set; } = "gemini-2.5-flash";
    public string ApiKey { get; set; } = string.Empty;
}

public interface IInvoiceAiSettingsStore
{
    InvoiceAiSettings? Load();
    void Save(InvoiceAiSettings settings);
}

public interface IInvoiceAiConnectionTester
{
    Task<Result> TestAsync(InvoiceAiSettings settings, CancellationToken cancellationToken = default);
}
