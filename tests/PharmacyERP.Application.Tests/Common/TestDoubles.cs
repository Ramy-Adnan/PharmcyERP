using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.Application.Common.Models;

namespace PharmacyERP.Application.Tests.Common;

/// <summary>A clock the tests control, so lockout-expiry and date-range logic can be exercised deterministically instead of depending on wall-clock timing.</summary>
public class FakeDateTime : IDateTime
{
    public DateTime UtcNow { get; set; } = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>A session stub. Audit stamping reads from this, so it just needs to answer consistently.</summary>
public class FakeCurrentUserService : ICurrentUserService
{
    private readonly List<string> _permissions = new();

    public int? UserId { get; private set; } = 1;
    public string? UserName { get; private set; } = "test-user";
    public int? CurrentBranchId { get; private set; } = 1;
    public IReadOnlyCollection<string> Permissions => _permissions.AsReadOnly();

    public bool HasPermission(string permissionCode) => true;

    public void SetSession(int userId, string userName, int branchId, IEnumerable<string> permissions)
    {
        UserId = userId;
        UserName = userName;
        CurrentBranchId = branchId;
        _permissions.Clear();
        _permissions.AddRange(permissions);
    }

    public void ClearSession()
    {
        UserId = null;
        UserName = null;
        CurrentBranchId = null;
        _permissions.Clear();
    }
}

/// <summary>
/// A deliberately trivial, fast password hasher. Real BCrypt at work factor 12 takes
/// ~250ms per call by design, which would make the lockout tests (6+ login attempts)
/// needlessly slow — the hashing algorithm itself is not what these tests are verifying.
/// </summary>
public class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string plainTextPassword) => "hashed:" + plainTextPassword;

    public bool Verify(string plainTextPassword, string hash) => hash == "hashed:" + plainTextPassword;
}

/// <summary>A license that always permits, so tests of BranchService/UserService business rules aren't masked by license limits.</summary>
public class PermissiveLicenseService : ILicenseService
{
    public string GetHardwareFingerprint() => "TEST-MACHINE";

    public Task<LicenseStatusDto> GetLicenseStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new LicenseStatusDto { IsPresent = true, IsValid = true });

    public Task<Result<LicenseInfoDto>> ActivateLicenseAsync(string licenseFileContent, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<LicenseInfoDto>.Success(new LicenseInfoDto()));

    public Task<bool> CanAddBranchAsync(int currentActiveBranchCount, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<bool> CanAddUserAsync(int currentActiveUserCount, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

/// <summary>A license that always refuses, used to prove the limit is actually enforced rather than merely present.</summary>
public class ExhaustedLicenseService : ILicenseService
{
    public string GetHardwareFingerprint() => "TEST-MACHINE";

    public Task<LicenseStatusDto> GetLicenseStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new LicenseStatusDto { IsPresent = true, IsValid = true });

    public Task<Result<LicenseInfoDto>> ActivateLicenseAsync(string licenseFileContent, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<LicenseInfoDto>.Success(new LicenseInfoDto()));

    public Task<bool> CanAddBranchAsync(int currentActiveBranchCount, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> CanAddUserAsync(int currentActiveUserCount, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
