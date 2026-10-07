namespace PharmacyERP.Application.Common.Interfaces;

/// <summary>Hashes and verifies user passwords. Implemented in Infrastructure using BCrypt.</summary>
public interface IPasswordHasher
{
    string Hash(string plainTextPassword);
    bool Verify(string plainTextPassword, string hash);
}
