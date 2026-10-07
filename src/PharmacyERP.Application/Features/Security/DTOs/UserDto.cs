using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Security.DTOs;

public class UserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public UserStatus Status { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? DefaultBranchName { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}
