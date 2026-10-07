namespace PharmacyERP.Application.Common.Interfaces;

/// <summary>
/// Exposes the identity of the currently logged-in user to the Application
/// layer (for audit stamping and authorization checks) without that layer
/// knowing anything about WPF or how the session is actually stored.
/// </summary>
public interface ICurrentUserService
{
    int? UserId { get; }
    string? UserName { get; }
    int? CurrentBranchId { get; }
    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permissionCode);

    void SetSession(int userId, string userName, int branchId, IEnumerable<string> permissions);
    void ClearSession();
}
