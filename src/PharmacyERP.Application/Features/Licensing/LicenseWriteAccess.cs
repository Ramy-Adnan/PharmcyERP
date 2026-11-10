namespace PharmacyERP.Application.Features.Licensing;

public static class LicenseWriteAccess
{
    public const string ReadOnlyMessage = "البرنامج في وضع القراءة فقط لعدم وجود ترخيص صالح. يمكنك عرض البيانات والنسخ الاحتياطي وتفعيل الترخيص من شاشة الترخيص.";

    public static async Task<string?> CheckAsync(ILicenseService licenseService, CancellationToken cancellationToken = default)
    {
        var status = await licenseService.GetLicenseStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.IsValid) return null;
        return string.IsNullOrWhiteSpace(status.Message) ? ReadOnlyMessage : $"{ReadOnlyMessage}{Environment.NewLine}{status.Message}";
    }

    public static async Task EnsureAsync(ILicenseService licenseService, CancellationToken cancellationToken = default)
    {
        var error = await CheckAsync(licenseService, cancellationToken).ConfigureAwait(false);
        if (error is not null) throw new LicenseWriteDeniedException(error);
    }
}

/// <summary>Expected, recoverable denial: reads and activation remain available.</summary>
public sealed class LicenseWriteDeniedException : InvalidOperationException
{
    public LicenseWriteDeniedException(string message) : base(message) { }
}
