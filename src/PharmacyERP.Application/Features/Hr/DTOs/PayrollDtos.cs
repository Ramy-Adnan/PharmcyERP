using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Hr.DTOs;

public class PayrollRunDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public PayrollRunStatus Status { get; set; }
    public int EmployeeCount { get; set; }
    public decimal TotalNetPay { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
}

public class PayrollRunLineDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public decimal BaseSalary { get; set; }
    public decimal TotalCommissions { get; set; }
    public decimal Deductions { get; set; }
    public string? DeductionNotes { get; set; }
    public decimal NetPay { get; set; }
}

public class PayrollRunDetailDto
{
    public PayrollRunDto Header { get; set; } = null!;
    public List<PayrollRunLineDto> Lines { get; set; } = new();
}

public class GeneratePayrollRunDto
{
    public int BranchId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string? Notes { get; set; }
}

public class UpdatePayrollLineDto
{
    public int PayrollRunLineId { get; set; }
    public decimal Deductions { get; set; }
    public string? DeductionNotes { get; set; }
}

public class MarkPayrollRunPaidDto
{
    public int PayrollRunId { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Today;
    public CashSourceType SourceType { get; set; } = CashSourceType.Cash;
    public int? CashBoxId { get; set; }
    public int? BankAccountId { get; set; }
}
