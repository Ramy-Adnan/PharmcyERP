namespace PharmacyERP.Application.Features.Security.DTOs;

public class UserUpsertDto
{
    public int? Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public int RoleId { get; set; }
    public int DefaultBranchId { get; set; }
    public List<int> AdditionalBranchIds { get; set; } = new();

    /// <summary>Only used when creating a new user, or when an admin explicitly resets a password.</summary>
    public string? Password { get; set; }
}
