using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>
/// An HR record for anyone the pharmacy employs — cashiers, pharmacists,
/// delivery staff, cleaners. Distinct from User: not every employee needs a
/// system login (a delivery driver doesn't touch the POS), so UserId is
/// optional and only set for employees who are also system Users. When it
/// is set, this Employee record is the HR/payroll side of that same person;
/// User remains the sole source of truth for authentication and permissions.
/// </summary>
public class Employee : AuditableEntity
{
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string JobTitle { get; set; } = string.Empty;

    public DateTime HireDate { get; set; } = DateTime.Today;
    public DateTime? TerminationDate { get; set; }

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    /// <summary>Set only if this employee also has a system login (cashier, pharmacist, manager, ...).</summary>
    public int? UserId { get; set; }
    public User? User { get; set; }

    public decimal MonthlyBaseSalary { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Attendance> AttendanceRecords { get; set; } = new List<Attendance>();
    public ICollection<Commission> Commissions { get; set; } = new List<Commission>();
    public ICollection<PayrollRunLine> PayrollRunLines { get; set; } = new List<PayrollRunLine>();
}
