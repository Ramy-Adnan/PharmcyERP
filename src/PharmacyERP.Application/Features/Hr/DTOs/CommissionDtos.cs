namespace PharmacyERP.Application.Features.Hr.DTOs;

public class CommissionDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? SalesInvoiceNumber { get; set; }
    public DateTime CommissionDate { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public bool IsPaid { get; set; }
}

public class CommissionCreateDto
{
    public int EmployeeId { get; set; }
    public int? SalesInvoiceId { get; set; }
    public DateTime CommissionDate { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}
