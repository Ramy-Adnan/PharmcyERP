using PharmacyERP.Application.Common.Interfaces;

namespace PharmacyERP.Infrastructure.Persistence.Seed;

/// <summary>The setup wizard's password is never written to a configuration file.</summary>
public sealed class InitialAdminPasswordProvider : IInitialAdminPasswordProvider
{
    private string? _password;
    public InitialAdminPasswordProvider(string? password = null) => _password = password;
    public string? GetPassword() => _password ?? Environment.GetEnvironmentVariable("PHARMACYERP_INITIAL_ADMIN_PASSWORD");
    public void Clear() => _password = null;
}
