using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Hr.DTOs;

namespace PharmacyERP.Application.Features.Hr;

/// <summary>
/// Full HR surface: Employees, Shifts, Attendance, Commissions, and Payroll
/// runs. Payroll is the only part that talks to Accounting — generating or
/// approving a run never posts anything; only MarkPayrollRunPaidAsync does,
/// via IAccountingService, mirroring how Sales/Purchasing/Insurance post
/// only at the exact moment money actually moves.
/// </summary>
public interface IHrService
{
    // Employees
    Task<List<EmployeeDto>> GetEmployeesAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<EmployeeUpsertDto?> GetEmployeeForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<List<LinkableUserDto>> GetLinkableUsersAsync(CancellationToken cancellationToken = default);
    Task<Result<EmployeeDto>> CreateEmployeeAsync(EmployeeUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<EmployeeDto>> UpdateEmployeeAsync(EmployeeUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SetEmployeeActiveStatusAsync(int employeeId, bool isActive, CancellationToken cancellationToken = default);

    // Shifts
    Task<List<ShiftDto>> GetShiftsAsync(CancellationToken cancellationToken = default);
    Task<ShiftUpsertDto?> GetShiftForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<ShiftDto>> CreateShiftAsync(ShiftUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<ShiftDto>> UpdateShiftAsync(ShiftUpsertDto dto, CancellationToken cancellationToken = default);

    // Attendance
    Task<List<AttendanceDto>> GetAttendanceAsync(DateTime fromDate, DateTime toDate, int? employeeId = null, CancellationToken cancellationToken = default);
    Task<Result<AttendanceDto>> RecordAttendanceAsync(AttendanceUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> CheckInAsync(int employeeId, CancellationToken cancellationToken = default);
    Task<Result> CheckOutAsync(int employeeId, CancellationToken cancellationToken = default);

    // Commissions
    Task<List<CommissionDto>> GetCommissionsAsync(int? employeeId = null, bool includePaid = true, CancellationToken cancellationToken = default);
    Task<Result<CommissionDto>> CreateCommissionAsync(CommissionCreateDto dto, CancellationToken cancellationToken = default);

    // Payroll
    Task<List<PayrollRunDto>> GetPayrollRunsAsync(CancellationToken cancellationToken = default);
    Task<PayrollRunDetailDto?> GetPayrollRunDetailAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<PayrollRunDto>> GeneratePayrollRunAsync(GeneratePayrollRunDto dto, CancellationToken cancellationToken = default);
    Task<Result> UpdatePayrollLineAsync(UpdatePayrollLineDto dto, CancellationToken cancellationToken = default);
    Task<Result> ApprovePayrollRunAsync(int payrollRunId, CancellationToken cancellationToken = default);
    Task<Result> MarkPayrollRunPaidAsync(MarkPayrollRunPaidDto dto, CancellationToken cancellationToken = default);
}
