using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// A system user (pharmacist, cashier, accountant, manager, admin, etc.).
/// Passwords are never stored in plain text — only the hash + salt produced
/// by the configured IPasswordHasher implementation (BCrypt).
/// </summary>
public class User : AuditableEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public UserStatus Status { get; set; } = UserStatus.Active;

    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>The branch the user is currently working from / defaults to on login.</summary>
    public int? DefaultBranchId { get; set; }
    public Branch? DefaultBranch { get; set; }

    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public ICollection<UserBranch> UserBranches { get; set; } = new List<UserBranch>();
    public ICollection<LoginHistory> LoginHistories { get; set; } = new List<LoginHistory>();

    public bool IsLockedOut() =>
        Status == UserStatus.Locked || (LockoutEndUtc.HasValue && LockoutEndUtc.Value > DateTime.UtcNow);
}
