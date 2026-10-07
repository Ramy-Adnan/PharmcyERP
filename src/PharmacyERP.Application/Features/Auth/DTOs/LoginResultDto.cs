namespace PharmacyERP.Application.Features.Auth.DTOs;

public class LoginResultDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;

    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();
    public List<BranchSummaryDto> AvailableBranches { get; set; } = new();
}

public class BranchSummaryDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
