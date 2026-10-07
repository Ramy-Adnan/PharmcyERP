using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Hr.DTOs;

public class AttendanceDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime AttendanceDate { get; set; }
    public DateTime? CheckInAtUtc { get; set; }
    public DateTime? CheckOutAtUtc { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Notes { get; set; }
}

public class AttendanceUpsertDto
{
    public int? Id { get; set; }
    public int EmployeeId { get; set; }
    public DateTime AttendanceDate { get; set; } = DateTime.Today;
    public DateTime? CheckInAtUtc { get; set; }
    public DateTime? CheckOutAtUtc { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
    public string? Notes { get; set; }
}
