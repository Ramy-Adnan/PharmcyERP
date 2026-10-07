namespace PharmacyERP.Application.Features.Auth.DTOs;

public class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional — if not supplied, the user's DefaultBranchId is used.</summary>
    public int? RequestedBranchId { get; set; }

    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }
}
