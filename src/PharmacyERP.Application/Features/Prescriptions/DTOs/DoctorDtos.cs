namespace PharmacyERP.Application.Features.Prescriptions.DTOs;

public class DoctorDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? LicenseNumber { get; set; }
    public string? Specialty { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
}

public class DoctorUpsertDto
{
    public int? Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? LicenseNumber { get; set; }
    public string? Specialty { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}
