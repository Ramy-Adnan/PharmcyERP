namespace PharmacyERP.Application.Features.Insurance.DTOs;

public class InsuranceCompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public bool IsActive { get; set; }
    public int PolicyCount { get; set; }
}

public class InsuranceCompanyUpsertDto
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public bool IsActive { get; set; } = true;
}
