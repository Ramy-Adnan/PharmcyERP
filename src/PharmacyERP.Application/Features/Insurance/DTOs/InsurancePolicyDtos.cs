namespace PharmacyERP.Application.Features.Insurance.DTOs;

public class InsurancePolicyDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string InsuranceCompanyName { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public decimal CoveragePercent { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
}

public class InsurancePolicyUpsertDto
{
    public int? Id { get; set; }
    public int CustomerId { get; set; }
    public int InsuranceCompanyId { get; set; }
    public string PolicyNumber { get; set; } = string.Empty;
    public decimal CoveragePercent { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}
