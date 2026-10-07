using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>Tracks every login attempt (successful or failed) for security review.</summary>
public class LoginHistory : BaseEntity
{
    public int? UserId { get; set; }
    public User? User { get; set; }

    public string UsernameAttempted { get; set; } = string.Empty;
    public bool WasSuccessful { get; set; }
    public string? FailureReason { get; set; }

    public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;
    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }
}
