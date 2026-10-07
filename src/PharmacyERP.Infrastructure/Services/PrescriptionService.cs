using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Prescriptions;
using PharmacyERP.Application.Features.Prescriptions.DTOs;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

public class PrescriptionService : IPrescriptionService
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTime _dateTime;

    public PrescriptionService(IApplicationDbContext context, IDateTime dateTime)
    {
        _context = context;
        _dateTime = dateTime;
    }

    // ===================== Doctors =====================

    public async Task<List<DoctorDto>> GetDoctorsAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var query = _context.Doctors.AsQueryable();
        if (!includeInactive) query = query.Where(d => d.IsActive);

        var doctors = await query.OrderBy(d => d.FullName).ToListAsync(cancellationToken);
        return doctors.Select(MapDoctorToDto).ToList();
    }

    public async Task<DoctorUpsertDto?> GetDoctorForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doctor is null) return null;

        return new DoctorUpsertDto
        {
            Id = doctor.Id,
            FullName = doctor.FullName,
            LicenseNumber = doctor.LicenseNumber,
            Specialty = doctor.Specialty,
            Phone = doctor.Phone,
            IsActive = doctor.IsActive
        };
    }

    public async Task<Result<DoctorDto>> CreateDoctorAsync(DoctorUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName)) return Result<DoctorDto>.Failure("اسم الطبيب مطلوب.");

        var doctor = new Doctor
        {
            FullName = dto.FullName.Trim(),
            LicenseNumber = dto.LicenseNumber,
            Specialty = dto.Specialty,
            Phone = dto.Phone,
            IsActive = dto.IsActive
        };

        _context.Doctors.Add(doctor);
        await _context.SaveChangesAsync(cancellationToken);
        return Result<DoctorDto>.Success(MapDoctorToDto(doctor));
    }

    public async Task<Result<DoctorDto>> UpdateDoctorAsync(DoctorUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<DoctorDto>.Failure("معرّف الطبيب مطلوب.");
        if (string.IsNullOrWhiteSpace(dto.FullName)) return Result<DoctorDto>.Failure("اسم الطبيب مطلوب.");

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == dto.Id, cancellationToken);
        if (doctor is null) return Result<DoctorDto>.Failure("الطبيب غير موجود.");

        doctor.FullName = dto.FullName.Trim();
        doctor.LicenseNumber = dto.LicenseNumber;
        doctor.Specialty = dto.Specialty;
        doctor.Phone = dto.Phone;
        doctor.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<DoctorDto>.Success(MapDoctorToDto(doctor));
    }

    public async Task<Result> SetDoctorActiveStatusAsync(int doctorId, bool isActive, CancellationToken cancellationToken = default)
    {
        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.Id == doctorId, cancellationToken);
        if (doctor is null) return Result.Failure("الطبيب غير موجود.");

        doctor.IsActive = isActive;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ===================== Prescriptions =====================

    public async Task<List<PrescriptionDto>> GetPrescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var prescriptions = await _context.Prescriptions
            .Include(p => p.Customer)
            .Include(p => p.Doctor)
            .Include(p => p.Branch)
            .Include(p => p.Items)
            .OrderByDescending(p => p.PrescriptionDate)
            .ToListAsync(cancellationToken);

        return prescriptions.Select(MapToDto).ToList();
    }

    public async Task<PrescriptionDetailDto?> GetPrescriptionDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var prescription = await _context.Prescriptions
            .Include(p => p.Customer)
            .Include(p => p.Doctor)
            .Include(p => p.Branch)
            .Include(p => p.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (prescription is null) return null;

        return new PrescriptionDetailDto
        {
            Header = MapToDto(prescription),
            Lines = prescription.Items.Select(MapLineToDto).ToList()
        };
    }

    public async Task<Result<PrescriptionDto>> CreatePrescriptionAsync(PrescriptionUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (!dto.Lines.Any()) return Result<PrescriptionDto>.Failure("يجب إضافة صنف واحد على الأقل للوصفة.");
        if (dto.Lines.Any(l => l.QuantityPrescribed <= 0)) return Result<PrescriptionDto>.Failure("يجب أن تكون كمية كل صنف أكبر من صفر.");

        var customerExists = await _context.Customers.AnyAsync(c => c.Id == dto.CustomerId, cancellationToken);
        if (!customerExists) return Result<PrescriptionDto>.Failure("العميل المحدد غير موجود.");

        var doctorExists = await _context.Doctors.AnyAsync(d => d.Id == dto.DoctorId, cancellationToken);
        if (!doctorExists) return Result<PrescriptionDto>.Failure("الطبيب المحدد غير موجود.");

        var prescription = new Prescription
        {
            Number = await GenerateNumberAsync(),
            CustomerId = dto.CustomerId,
            DoctorId = dto.DoctorId,
            BranchId = dto.BranchId,
            PrescriptionDate = dto.PrescriptionDate,
            ExpiryDate = dto.ExpiryDate,
            Status = PrescriptionStatus.Active,
            Notes = dto.Notes
        };

        _context.Prescriptions.Add(prescription);
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var line in dto.Lines)
        {
            _context.PrescriptionItems.Add(new PrescriptionItem
            {
                PrescriptionId = prescription.Id,
                ItemId = line.ItemId,
                QuantityPrescribed = line.QuantityPrescribed,
                DosageInstructions = line.DosageInstructions
            });
        }
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.Prescriptions
            .Include(p => p.Customer).Include(p => p.Doctor).Include(p => p.Branch).Include(p => p.Items)
            .FirstAsync(p => p.Id == prescription.Id, cancellationToken);

        return Result<PrescriptionDto>.Success(MapToDto(reloaded));
    }

    public async Task<Result> CancelPrescriptionAsync(int prescriptionId, CancellationToken cancellationToken = default)
    {
        var prescription = await _context.Prescriptions.FirstOrDefaultAsync(p => p.Id == prescriptionId, cancellationToken);
        if (prescription is null) return Result.Failure("الوصفة غير موجودة.");

        if (prescription.Status == PrescriptionStatus.Fulfilled)
            return Result.Failure("لا يمكن إلغاء وصفة تم صرفها بالكامل.");

        prescription.Status = PrescriptionStatus.Cancelled;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<List<ActivePrescriptionSummaryDto>> GetFillablePrescriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var today = _dateTime.UtcNow.Date;

        var prescriptions = await _context.Prescriptions
            .Include(p => p.Items).ThenInclude(i => i.Item)
            .Where(p => p.CustomerId == customerId
                && (p.Status == PrescriptionStatus.Active || p.Status == PrescriptionStatus.PartiallyFulfilled)
                && (!p.ExpiryDate.HasValue || p.ExpiryDate.Value.Date >= today))
            .OrderByDescending(p => p.PrescriptionDate)
            .ToListAsync(cancellationToken);

        return prescriptions.Select(p => new ActivePrescriptionSummaryDto
        {
            Id = p.Id,
            Number = p.Number,
            PrescriptionDate = p.PrescriptionDate,
            Status = p.Status,
            Lines = p.Items.Select(MapLineToDto).ToList()
        }).ToList();
    }

    private async Task<string> GenerateNumberAsync()
    {
        var count = await _context.Prescriptions.CountAsync();
        return $"RX-{DateTime.UtcNow:yyyyMM}-{count + 1:D5}";
    }

    private static PrescriptionDto MapToDto(Prescription p) => new()
    {
        Id = p.Id,
        Number = p.Number,
        CustomerName = p.Customer.Name,
        DoctorName = p.Doctor.FullName,
        BranchName = p.Branch.Name,
        PrescriptionDate = p.PrescriptionDate,
        ExpiryDate = p.ExpiryDate,
        Status = p.Status,
        Notes = p.Notes,
        LineCount = p.Items.Count
    };

    private static PrescriptionLineDto MapLineToDto(PrescriptionItem i) => new()
    {
        Id = i.Id,
        ItemId = i.ItemId,
        ItemName = i.Item.Name,
        QuantityPrescribed = i.QuantityPrescribed,
        QuantityDispensed = i.QuantityDispensed,
        QuantityRemaining = i.QuantityRemaining,
        DosageInstructions = i.DosageInstructions
    };

    private static DoctorDto MapDoctorToDto(Doctor d) => new()
    {
        Id = d.Id,
        FullName = d.FullName,
        LicenseNumber = d.LicenseNumber,
        Specialty = d.Specialty,
        Phone = d.Phone,
        IsActive = d.IsActive
    };
}
