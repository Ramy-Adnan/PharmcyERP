using PharmacyERP.Application.Common.Interfaces;

namespace PharmacyERP.Infrastructure.Identity;

/// <summary>
/// Holds the active session for the lifetime of the running WPF process.
/// Registered as a Singleton in DI — there is exactly one logged-in user
/// per desktop application instance.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly object _lock = new();
    private List<string> _permissions = new();

    public int? UserId { get; private set; }
    public string? UserName { get; private set; }
    public int? CurrentBranchId { get; private set; }
    public IReadOnlyCollection<string> Permissions => _permissions.AsReadOnly();

    public bool HasPermission(string permissionCode)
    {
        lock (_lock)
        {
            return _permissions.Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetSession(int userId, string userName, int branchId, IEnumerable<string> permissions)
    {
        lock (_lock)
        {
            UserId = userId;
            UserName = userName;
            CurrentBranchId = branchId;
            _permissions = permissions.ToList();
        }
    }

    public void ClearSession()
    {
        lock (_lock)
        {
            UserId = null;
            UserName = null;
            CurrentBranchId = null;
            _permissions.Clear();
        }
    }
}
