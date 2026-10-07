using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

public class HrService : IHrService
{
    private readonly IApplicationDbContext _context;
    private readonly IAccountingService _accountingService;
    private readonly IDateTime _dateTime;

    public HrService(IApplicationDbContext context, IAccountingService accountingService, IDateTime dateTime)
    {
        _context = context;
        _accountingService = accountingService;
        _dateTime = dateTime;
    }

    // ===================== Employees =====================

    public async Task<List<EmployeeDto>> GetEmployeesAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var query = _context.Employees
            .Include(e => e.Branch)
            .Include(e => e.Shift)
            .Include(e => e.User)
            .AsQueryable();

        if (!includeInactive) query = query.Where(e => e.IsActive);

        var employees = await query.OrderBy(e => e.FullName).ToListAsync(cancellationToken);
        return employees.Select(MapToDto).ToList();
    }

    public async Task<EmployeeUpsertDto?> GetEmployeeForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null) return null;

        return new EmployeeUpsertDto
        {
            Id = employee.Id,
            EmployeeCode = employee.EmployeeCode,
            FullName = employee.FullName,
            NationalId = employee.NationalId,
            Phone = employee.Phone,
            Address = employee.Address,
            JobTitle = employee.JobTitle,
            HireDate = employee.HireDate,
            TerminationDate = employee.TerminationDate,
            BranchId = employee.BranchId,
            ShiftId = employee.ShiftId,
            UserId = employee.UserId,
            MonthlyBaseSalary = employee.MonthlyBaseSalary,
            IsActive = employee.IsActive
        };
    }

    public async Task<List<LinkableUserDto>> GetLinkableUsersAsync(CancellationToken cancellationToken = default)
    {
        var linkedUserIds = await _context.Employees.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value).ToListAsync(cancellationToken);

        var users = await _context.Users
            .Where(u => !linkedUserIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        return users.Select(u => new LinkableUserDto { Id = u.Id, FullName = u.FullName, Username = u.Username }).ToList();
    }

    public async Task<Result<EmployeeDto>> CreateEmployeeAsync(EmployeeUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateEmployeeAsync(dto, cancellationToken);
        if (validation is not null) return Result<EmployeeDto>.Failure(validation);

        var employee = new Employee
        {
            EmployeeCode = dto.EmployeeCode.Trim().ToUpperInvariant(),
            FullName = dto.FullName.Trim(),
            NationalId = dto.NationalId,
            Phone = dto.Phone,
            Address = dto.Address,
            JobTitle = dto.JobTitle.Trim(),
            HireDate = dto.HireDate,
            TerminationDate = dto.TerminationDate,
            BranchId = dto.BranchId,
            ShiftId = dto.ShiftId,
            UserId = dto.UserId,
            MonthlyBaseSalary = dto.MonthlyBaseSalary,
            IsActive = dto.IsActive
        };

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<EmployeeDto>.Success((await GetEmployeesAsync(cancellationToken: cancellationToken)).First(e => e.Id == employee.Id));
    }

    public async Task<Result<EmployeeDto>> UpdateEmployeeAsync(EmployeeUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<EmployeeDto>.Failure("معرّف الموظف مطلوب.");

        var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == dto.Id, cancellationToken);
        if (employee is null) return Result<EmployeeDto>.Failure("الموظف غير موجود.");

        var validation = await ValidateEmployeeAsync(dto, cancellationToken);
        if (validation is not null) return Result<EmployeeDto>.Failure(validation);

        employee.EmployeeCode = dto.EmployeeCode.Trim().ToUpperInvariant();
        employee.FullName = dto.FullName.Trim();
        employee.NationalId = dto.NationalId;
        employee.Phone = dto.Phone;
        employee.Address = dto.Address;
        employee.JobTitle = dto.JobTitle.Trim();
        employee.HireDate = dto.HireDate;
        employee.TerminationDate = dto.TerminationDate;
        employee.BranchId = dto.BranchId;
        employee.ShiftId = dto.ShiftId;
        employee.UserId = dto.UserId;
        employee.MonthlyBaseSalary = dto.MonthlyBaseSalary;
        employee.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<EmployeeDto>.Success((await GetEmployeesAsync(cancellationToken: cancellationToken)).First(e => e.Id == employee.Id));
    }

    public async Task<Result> SetEmployeeActiveStatusAsync(int employeeId, bool isActive, CancellationToken cancellationToken = default)
    {
        var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null) return Result.Failure("الموظف غير موجود.");

        employee.IsActive = isActive;
        if (!isActive && employee.TerminationDate is null) employee.TerminationDate = _dateTime.UtcNow.Date;

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ===================== Shifts =====================

    public async Task<List<ShiftDto>> GetShiftsAsync(CancellationToken cancellationToken = default)
    {
        var shifts = await _context.Shifts.Include(s => s.Employees).OrderBy(s => s.StartTime).ToListAsync(cancellationToken);
        return shifts.Select(s => new ShiftDto
        {
            Id = s.Id,
            Name = s.Name,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            IsActive = s.IsActive,
            EmployeeCount = s.Employees.Count
        }).ToList();
    }

    public async Task<ShiftUpsertDto?> GetShiftForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var shift = await _context.Shifts.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shift is null) return null;

        return new ShiftUpsertDto { Id = shift.Id, Name = shift.Name, StartTime = shift.StartTime, EndTime = shift.EndTime, IsActive = shift.IsActive };
    }

    public async Task<Result<ShiftDto>> CreateShiftAsync(ShiftUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<ShiftDto>.Failure("اسم الوردية مطلوب.");
        if (dto.EndTime <= dto.StartTime) return Result<ShiftDto>.Failure("وقت النهاية يجب أن يكون بعد وقت البداية.");

        var nameTaken = await _context.Shifts.AnyAsync(s => s.Name == dto.Name.Trim(), cancellationToken);
        if (nameTaken) return Result<ShiftDto>.Failure("اسم الوردية مستخدم مسبقاً.");

        var shift = new Shift { Name = dto.Name.Trim(), StartTime = dto.StartTime, EndTime = dto.EndTime, IsActive = dto.IsActive };
        _context.Shifts.Add(shift);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<ShiftDto>.Success(new ShiftDto { Id = shift.Id, Name = shift.Name, StartTime = shift.StartTime, EndTime = shift.EndTime, IsActive = shift.IsActive, EmployeeCount = 0 });
    }

    public async Task<Result<ShiftDto>> UpdateShiftAsync(ShiftUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<ShiftDto>.Failure("معرّف الوردية مطلوب.");
        if (dto.EndTime <= dto.StartTime) return Result<ShiftDto>.Failure("وقت النهاية يجب أن يكون بعد وقت البداية.");

        var shift = await _context.Shifts.Include(s => s.Employees).FirstOrDefaultAsync(s => s.Id == dto.Id, cancellationToken);
        if (shift is null) return Result<ShiftDto>.Failure("الوردية غير موجودة.");

        var nameTaken = await _context.Shifts.AnyAsync(s => s.Name == dto.Name.Trim() && s.Id != dto.Id, cancellationToken);
        if (nameTaken) return Result<ShiftDto>.Failure("اسم الوردية مستخدم مسبقاً.");

        shift.Name = dto.Name.Trim();
        shift.StartTime = dto.StartTime;
        shift.EndTime = dto.EndTime;
        shift.IsActive = dto.IsActive;
        await _context.SaveChangesAsync(cancellationToken);

        return Result<ShiftDto>.Success(new ShiftDto { Id = shift.Id, Name = shift.Name, StartTime = shift.StartTime, EndTime = shift.EndTime, IsActive = shift.IsActive, EmployeeCount = shift.Employees.Count });
    }

    // ===================== Attendance =====================

    public async Task<List<AttendanceDto>> GetAttendanceAsync(DateTime fromDate, DateTime toDate, int? employeeId = null, CancellationToken cancellationToken = default)
    {
        var rangeEnd = toDate.Date.AddDays(1);

        var query = _context.Attendances
            .Include(a => a.Employee)
            .Where(a => a.AttendanceDate >= fromDate.Date && a.AttendanceDate < rangeEnd);

        if (employeeId.HasValue) query = query.Where(a => a.EmployeeId == employeeId.Value);

        var records = await query.OrderByDescending(a => a.AttendanceDate).ThenBy(a => a.Employee.FullName).ToListAsync(cancellationToken);

        return records.Select(a => new AttendanceDto
        {
            Id = a.Id,
            EmployeeId = a.EmployeeId,
            EmployeeName = a.Employee.FullName,
            AttendanceDate = a.AttendanceDate,
            CheckInAtUtc = a.CheckInAtUtc,
            CheckOutAtUtc = a.CheckOutAtUtc,
            Status = a.Status,
            Notes = a.Notes
        }).ToList();
    }

    public async Task<Result<AttendanceDto>> RecordAttendanceAsync(AttendanceUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, cancellationToken);
        if (employee is null) return Result<AttendanceDto>.Failure("الموظف غير موجود.");

        Attendance attendance;
        if (dto.Id.HasValue)
        {
            var existing = await _context.Attendances.Include(a => a.Employee).FirstOrDefaultAsync(a => a.Id == dto.Id, cancellationToken);
            if (existing is null) return Result<AttendanceDto>.Failure("سجل الحضور غير موجود.");
            attendance = existing;
        }
        else
        {
            var duplicateExists = await _context.Attendances
                .AnyAsync(a => a.EmployeeId == dto.EmployeeId && a.AttendanceDate == dto.AttendanceDate.Date, cancellationToken);
            if (duplicateExists) return Result<AttendanceDto>.Failure("يوجد سجل حضور لهذا الموظف في هذا التاريخ بالفعل.");

            attendance = new Attendance { EmployeeId = dto.EmployeeId, Employee = employee };
            _context.Attendances.Add(attendance);
        }

        attendance.AttendanceDate = dto.AttendanceDate.Date;
        attendance.CheckInAtUtc = dto.CheckInAtUtc;
        attendance.CheckOutAtUtc = dto.CheckOutAtUtc;
        attendance.Status = dto.Status;
        attendance.Notes = dto.Notes;

        await _context.SaveChangesAsync(cancellationToken);

        return Result<AttendanceDto>.Success(new AttendanceDto
        {
            Id = attendance.Id,
            EmployeeId = attendance.EmployeeId,
            EmployeeName = employee.FullName,
            AttendanceDate = attendance.AttendanceDate,
            CheckInAtUtc = attendance.CheckInAtUtc,
            CheckOutAtUtc = attendance.CheckOutAtUtc,
            Status = attendance.Status,
            Notes = attendance.Notes
        });
    }

    public async Task<Result> CheckInAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        var today = _dateTime.UtcNow.Date;

        var attendance = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.AttendanceDate == today, cancellationToken);
        if (attendance is null)
        {
            attendance = new Attendance { EmployeeId = employeeId, AttendanceDate = today, Status = AttendanceStatus.Present };
            _context.Attendances.Add(attendance);
        }

        if (attendance.CheckInAtUtc.HasValue) return Result.Failure("تم تسجيل حضور هذا الموظف اليوم بالفعل.");

        attendance.CheckInAtUtc = _dateTime.UtcNow;
        attendance.Status = AttendanceStatus.Present;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CheckOutAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        var today = _dateTime.UtcNow.Date;

        var attendance = await _context.Attendances.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.AttendanceDate == today, cancellationToken);
        if (attendance is null || !attendance.CheckInAtUtc.HasValue) return Result.Failure("لم يتم تسجيل حضور هذا الموظف اليوم بعد.");
        if (attendance.CheckOutAtUtc.HasValue) return Result.Failure("تم تسجيل انصراف هذا الموظف اليوم بالفعل.");

        attendance.CheckOutAtUtc = _dateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ===================== Commissions =====================

    public async Task<List<CommissionDto>> GetCommissionsAsync(int? employeeId = null, bool includePaid = true, CancellationToken cancellationToken = default)
    {
        var query = _context.Commissions.Include(c => c.Employee).Include(c => c.SalesInvoice).AsQueryable();

        if (employeeId.HasValue) query = query.Where(c => c.EmployeeId == employeeId.Value);
        if (!includePaid) query = query.Where(c => !c.IsPaid);

        var commissions = await query.OrderByDescending(c => c.CommissionDate).ToListAsync(cancellationToken);

        return commissions.Select(c => new CommissionDto
        {
            Id = c.Id,
            EmployeeId = c.EmployeeId,
            EmployeeName = c.Employee.FullName,
            SalesInvoiceNumber = c.SalesInvoice?.Number,
            CommissionDate = c.CommissionDate,
            Amount = c.Amount,
            Notes = c.Notes,
            IsPaid = c.IsPaid
        }).ToList();
    }

    public async Task<Result<CommissionDto>> CreateCommissionAsync(CommissionCreateDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Amount <= 0) return Result<CommissionDto>.Failure("مبلغ العمولة يجب أن يكون أكبر من صفر.");

        var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, cancellationToken);
        if (employee is null) return Result<CommissionDto>.Failure("الموظف غير موجود.");

        if (dto.SalesInvoiceId.HasValue)
        {
            var invoiceExists = await _context.SalesInvoices.AnyAsync(i => i.Id == dto.SalesInvoiceId.Value, cancellationToken);
            if (!invoiceExists) return Result<CommissionDto>.Failure("فاتورة المبيعات المحددة غير موجودة.");
        }

        var commission = new Commission
        {
            EmployeeId = dto.EmployeeId,
            SalesInvoiceId = dto.SalesInvoiceId,
            CommissionDate = dto.CommissionDate,
            Amount = dto.Amount,
            Notes = dto.Notes,
            IsPaid = false
        };

        _context.Commissions.Add(commission);
        await _context.SaveChangesAsync(cancellationToken);

        var invoiceNumber = dto.SalesInvoiceId.HasValue
            ? (await _context.SalesInvoices.FirstAsync(i => i.Id == dto.SalesInvoiceId.Value, cancellationToken)).Number
            : null;

        return Result<CommissionDto>.Success(new CommissionDto
        {
            Id = commission.Id, EmployeeId = commission.EmployeeId, EmployeeName = employee.FullName,
            SalesInvoiceNumber = invoiceNumber, CommissionDate = commission.CommissionDate,
            Amount = commission.Amount, Notes = commission.Notes, IsPaid = false
        });
    }

    // ===================== Payroll =====================

    public async Task<List<PayrollRunDto>> GetPayrollRunsAsync(CancellationToken cancellationToken = default)
    {
        var runs = await _context.PayrollRuns
            .Include(p => p.Branch)
            .Include(p => p.Lines)
            .OrderByDescending(p => p.PeriodStart)
            .ToListAsync(cancellationToken);

        return runs.Select(MapRunToDto).ToList();
    }

    public async Task<PayrollRunDetailDto?> GetPayrollRunDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var run = await _context.PayrollRuns
            .Include(p => p.Branch)
            .Include(p => p.Lines).ThenInclude(l => l.Employee)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (run is null) return null;

        return new PayrollRunDetailDto
        {
            Header = MapRunToDto(run),
            Lines = run.Lines.Select(MapLineToDto).OrderBy(l => l.EmployeeName).ToList()
        };
    }

    public async Task<Result<PayrollRunDto>> GeneratePayrollRunAsync(GeneratePayrollRunDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.PeriodEnd.Date < dto.PeriodStart.Date) return Result<PayrollRunDto>.Failure("تاريخ نهاية الفترة لا يمكن أن يسبق تاريخ البداية.");

        var branchExists = await _context.Branches.AnyAsync(b => b.Id == dto.BranchId, cancellationToken);
        if (!branchExists) return Result<PayrollRunDto>.Failure("الفرع غير موجود.");

        var overlapping = await _context.PayrollRuns.AnyAsync(p =>
            p.BranchId == dto.BranchId && p.PeriodStart.Date == dto.PeriodStart.Date && p.PeriodEnd.Date == dto.PeriodEnd.Date, cancellationToken);
        if (overlapping) return Result<PayrollRunDto>.Failure("توجد دورة رواتب لهذا الفرع بنفس الفترة بالضبط بالفعل.");

        var employees = await _context.Employees
            .Where(e => e.BranchId == dto.BranchId && e.IsActive)
            .ToListAsync(cancellationToken);

        if (!employees.Any()) return Result<PayrollRunDto>.Failure("لا يوجد موظفون نشطون في هذا الفرع.");

        var run = new PayrollRun
        {
            Number = await GenerateNumberAsync(),
            BranchId = dto.BranchId,
            PeriodStart = dto.PeriodStart.Date,
            PeriodEnd = dto.PeriodEnd.Date,
            Status = PayrollRunStatus.Draft,
            GeneratedAtUtc = _dateTime.UtcNow,
            Notes = dto.Notes
        };
        _context.PayrollRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken);

        var rangeEnd = dto.PeriodEnd.Date.AddDays(1);

        foreach (var employee in employees)
        {
            var unpaidCommissions = await _context.Commissions
                .Where(c => c.EmployeeId == employee.Id && !c.IsPaid && c.CommissionDate >= dto.PeriodStart.Date && c.CommissionDate < rangeEnd)
                .ToListAsync(cancellationToken);

            var line = new PayrollRunLine
            {
                PayrollRunId = run.Id,
                EmployeeId = employee.Id,
                BaseSalary = employee.MonthlyBaseSalary,
                TotalCommissions = unpaidCommissions.Sum(c => c.Amount),
                Deductions = 0
            };
            _context.PayrollRunLines.Add(line);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var commission in unpaidCommissions)
                commission.PayrollRunLineId = line.Id;

            await _context.SaveChangesAsync(cancellationToken);
        }

        var reloaded = await _context.PayrollRuns.Include(p => p.Branch).Include(p => p.Lines).FirstAsync(p => p.Id == run.Id, cancellationToken);
        return Result<PayrollRunDto>.Success(MapRunToDto(reloaded));
    }

    public async Task<Result> UpdatePayrollLineAsync(UpdatePayrollLineDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Deductions < 0) return Result.Failure("قيمة الاستقطاع لا يمكن أن تكون سالبة.");

        var line = await _context.PayrollRunLines.Include(l => l.PayrollRun).FirstOrDefaultAsync(l => l.Id == dto.PayrollRunLineId, cancellationToken);
        if (line is null) return Result.Failure("سطر الراتب غير موجود.");

        if (line.PayrollRun.Status != PayrollRunStatus.Draft)
            return Result.Failure("لا يمكن تعديل دورة رواتب بعد اعتمادها.");

        if (dto.Deductions > line.BaseSalary + line.TotalCommissions)
            return Result.Failure("قيمة الاستقطاع لا يمكن أن تتجاوز إجمالي الراتب الأساسي والعمولات.");

        line.Deductions = dto.Deductions;
        line.DeductionNotes = dto.DeductionNotes;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ApprovePayrollRunAsync(int payrollRunId, CancellationToken cancellationToken = default)
    {
        var run = await _context.PayrollRuns.FirstOrDefaultAsync(p => p.Id == payrollRunId, cancellationToken);
        if (run is null) return Result.Failure("دورة الرواتب غير موجودة.");
        if (run.Status != PayrollRunStatus.Draft) return Result.Failure("لا يمكن اعتماد دورة رواتب إلا وهي بحالة مسودة.");

        run.Status = PayrollRunStatus.Approved;
        run.ApprovedAtUtc = _dateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> MarkPayrollRunPaidAsync(MarkPayrollRunPaidDto dto, CancellationToken cancellationToken = default)
    {
        var run = await _context.PayrollRuns.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == dto.PayrollRunId, cancellationToken);
        if (run is null) return Result.Failure("دورة الرواتب غير موجودة.");
        if (run.Status != PayrollRunStatus.Approved) return Result.Failure("لا يمكن صرف دورة رواتب إلا بعد اعتمادها.");

        if (dto.SourceType == CashSourceType.Cash && dto.CashBoxId is null) return Result.Failure("الرجاء اختيار الصندوق النقدي.");
        if (dto.SourceType == CashSourceType.Bank && dto.BankAccountId is null) return Result.Failure("الرجاء اختيار الحساب البنكي.");

        var totalNetPay = run.Lines.Sum(l => l.NetPay);
        if (totalNetPay <= 0) return Result.Failure("لا يوجد صافي راتب مستحق الصرف في هذه الدورة.");

        run.Status = PayrollRunStatus.Paid;
        run.PaidAtUtc = _dateTime.UtcNow;
        run.CashBoxId = dto.SourceType == CashSourceType.Cash ? dto.CashBoxId : null;
        run.BankAccountId = dto.SourceType == CashSourceType.Bank ? dto.BankAccountId : null;

        var lineIds = run.Lines.Select(l => l.Id).ToList();
        var paidCommissions = await _context.Commissions
            .Where(c => c.PayrollRunLineId != null && lineIds.Contains(c.PayrollRunLineId!.Value))
            .ToListAsync(cancellationToken);

        foreach (var commission in paidCommissions)
            commission.IsPaid = true;

        await _context.SaveChangesAsync(cancellationToken);

        await _accountingService.PostPayrollPaymentAsync(new PayrollPaymentPostingRequest
        {
            BranchId = run.BranchId,
            PayrollRunId = run.Id,
            PayrollRunNumber = run.Number,
            PaymentDate = dto.PaymentDate,
            Amount = totalNetPay
        }, cancellationToken);

        return Result.Success();
    }

    private async Task<string> GenerateNumberAsync()
    {
        var count = await _context.PayrollRuns.CountAsync();
        return $"PR-{DateTime.UtcNow:yyyyMM}-{count + 1:D4}";
    }

    private async Task<string?> ValidateEmployeeAsync(EmployeeUpsertDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.EmployeeCode)) return "رمز الموظف مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.FullName)) return "اسم الموظف مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.JobTitle)) return "المسمى الوظيفي مطلوب.";
        if (dto.MonthlyBaseSalary < 0) return "الراتب الأساسي لا يمكن أن يكون سالباً.";

        var codeTaken = await _context.Employees.AnyAsync(e => e.EmployeeCode == dto.EmployeeCode.Trim().ToUpper() && e.Id != dto.Id, cancellationToken);
        if (codeTaken) return "رمز الموظف مستخدم مسبقاً.";

        var branchExists = await _context.Branches.AnyAsync(b => b.Id == dto.BranchId, cancellationToken);
        if (!branchExists) return "الفرع المحدد غير موجود.";

        if (dto.ShiftId.HasValue)
        {
            var shiftExists = await _context.Shifts.AnyAsync(s => s.Id == dto.ShiftId.Value, cancellationToken);
            if (!shiftExists) return "الوردية المحددة غير موجودة.";
        }

        if (dto.UserId.HasValue)
        {
            var userLinkedElsewhere = await _context.Employees.AnyAsync(e => e.UserId == dto.UserId.Value && e.Id != dto.Id, cancellationToken);
            if (userLinkedElsewhere) return "هذا المستخدم مرتبط بموظف آخر بالفعل.";
        }

        return null;
    }

    private static EmployeeDto MapToDto(Employee e) => new()
    {
        Id = e.Id,
        EmployeeCode = e.EmployeeCode,
        FullName = e.FullName,
        NationalId = e.NationalId,
        Phone = e.Phone,
        JobTitle = e.JobTitle,
        BranchName = e.Branch.Name,
        ShiftName = e.Shift?.Name,
        LinkedUsername = e.User?.Username,
        HireDate = e.HireDate,
        TerminationDate = e.TerminationDate,
        MonthlyBaseSalary = e.MonthlyBaseSalary,
        IsActive = e.IsActive
    };

    private static PayrollRunDto MapRunToDto(PayrollRun p) => new()
    {
        Id = p.Id,
        Number = p.Number,
        PeriodStart = p.PeriodStart,
        PeriodEnd = p.PeriodEnd,
        BranchName = p.Branch.Name,
        Status = p.Status,
        EmployeeCount = p.Lines.Count,
        TotalNetPay = p.Lines.Sum(l => l.NetPay),
        GeneratedAtUtc = p.GeneratedAtUtc,
        ApprovedAtUtc = p.ApprovedAtUtc,
        PaidAtUtc = p.PaidAtUtc
    };

    private static PayrollRunLineDto MapLineToDto(PayrollRunLine l) => new()
    {
        Id = l.Id,
        EmployeeId = l.EmployeeId,
        EmployeeName = l.Employee.FullName,
        JobTitle = l.Employee.JobTitle,
        BaseSalary = l.BaseSalary,
        TotalCommissions = l.TotalCommissions,
        Deductions = l.Deductions,
        DeductionNotes = l.DeductionNotes,
        NetPay = l.NetPay
    };
}
