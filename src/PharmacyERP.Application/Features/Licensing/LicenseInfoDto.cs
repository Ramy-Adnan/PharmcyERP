namespace PharmacyERP.Application.Features.Licensing;

public class LicenseInfoDto
{
    public string LicenseKey { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int MaxBranches { get; set; }
    public int MaxUsers { get; set; }
    public string HardwareId { get; set; } = string.Empty;
}

/// <summary>Result of validating the license currently installed on this machine.</summary>
public class LicenseStatusDto
{
    public bool IsPresent { get; set; }
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public bool IsHardwareMismatch { get; set; }
    public int? DaysUntilExpiry { get; set; }
    public string? Message { get; set; }
    public LicenseInfoDto? License { get; set; }
}
