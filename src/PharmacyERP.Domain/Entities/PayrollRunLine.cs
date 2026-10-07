using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>One employee's pay computation within a PayrollRun: base salary + accumulated unpaid commissions - manual deductions = net pay.</summary>
public class PayrollRunLine : AuditableEntity
{
    public int PayrollRunId { get; set; }
    public PayrollRun PayrollRun { get; set; } = null!;

    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public decimal BaseSalary { get; set; }
    public decimal TotalCommissions { get; set; }
    public decimal Deductions { get; set; }
    public string? DeductionNotes { get; set; }

    public decimal NetPay => Math.Max(0, BaseSalary + TotalCommissions - Deductions);

    public ICollection<Commission> Commissions { get; set; } = new List<Commission>();
}
