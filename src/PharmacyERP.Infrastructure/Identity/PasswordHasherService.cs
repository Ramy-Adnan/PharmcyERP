using PharmacyERP.Application.Common.Interfaces;

namespace PharmacyERP.Infrastructure.Identity;

/// <summary>BCrypt-based password hashing — work factor 12 balances security with login latency.</summary>
public class PasswordHasherService : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string plainTextPassword) =>
        BCrypt.Net.BCrypt.HashPassword(plainTextPassword, workFactor: WorkFactor);

    public bool Verify(string plainTextPassword, string hash) =>
        BCrypt.Net.BCrypt.Verify(plainTextPassword, hash);
}
