namespace PharmacyERP.Application.Common.Interfaces;

/// <summary>Supplies the first administrator's password in memory during provisioning only.</summary>
public interface IInitialAdminPasswordProvider
{
    string? GetPassword();
    void Clear();
}
