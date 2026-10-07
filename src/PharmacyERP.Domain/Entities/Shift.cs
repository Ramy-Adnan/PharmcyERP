using PharmacyERP.Domain.Common;

namespace PharmacyERP.Domain.Entities;

/// <summary>A named work-shift pattern (e.g. "Morning 8-4", "Evening 4-12") used as an employee's expected schedule for attendance/lateness reference.</summary>
public class Shift : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
