namespace PharmacyERP.Application.Features.Hr.DTOs;

public class EmployeeDto
{
    public int Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string? ShiftName { get; set; }
    public string? LinkedUsername { get; set; }
    public DateTime HireDate { get; set; }
    public DateTime? TerminationDate { get; set; }
    public decimal MonthlyBaseSalary { get; set; }
    public bool IsActive { get; set; }
}

public class EmployeeUpsertDto
{
    public int? Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public DateTime HireDate { get; set; } = DateTime.Today;
    public DateTime? TerminationDate { get; set; }
    public int BranchId { get; set; }
    public int? ShiftId { get; set; }
    public int? UserId { get; set; }
    public decimal MonthlyBaseSalary { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Lightweight option for linking an Employee to an existing system User account.</summary>
public class LinkableUserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
