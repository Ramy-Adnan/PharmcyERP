using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Branches;
using PharmacyERP.Application.Features.Branches.DTOs;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Services;

public class BranchService : IBranchService
{
    private readonly IApplicationDbContext _context;
    private readonly ILicenseService _licenseService;

    public BranchService(IApplicationDbContext context, ILicenseService licenseService)
    {
        _context = context;
        _licenseService = licenseService;
    }

    public async Task<List<BranchDto>> GetAllAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var query = _context.Branches
            .Include(b => b.Warehouses)
            .Include(b => b.UserBranches)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(b => b.IsActive);

        var branches = await query.OrderByDescending(b => b.IsMainBranch).ThenBy(b => b.Name).ToListAsync(cancellationToken);

        return branches.Select(MapToDto).ToList();
    }

    public async Task<BranchDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var branch = await _context.Branches
            .Include(b => b.Warehouses)
            .Include(b => b.UserBranches)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        return branch is null ? null : MapToDto(branch);
    }

    public async Task<Result<BranchDto>> CreateAsync(BranchUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(dto, cancellationToken);
        if (validation is not null) return Result<BranchDto>.Failure(validation);

        var activeBranchCount = await _context.Branches.CountAsync(b => b.IsActive, cancellationToken);
        //if (!await _licenseService.CanAddBranchAsync(activeBranchCount, cancellationToken))
        //    return Result<BranchDto>.Failure("تم الوصول إلى الحد الأقصى لعدد الفروع المسموح به بموجب الترخيص الحالي. الرجاء التواصل مع الجهة المرخِّصة لترقية الترخيص.");

        var branch = new Branch
        {
            Code = dto.Code.Trim().ToUpperInvariant(),
            Name = dto.Name.Trim(),
            Type = dto.Type,
            Address = dto.Address,
            Phone = dto.Phone,
            TaxRegistrationNumber = dto.TaxRegistrationNumber,
            LicenseNumber = dto.LicenseNumber,
            IsActive = dto.IsActive
        };

        _context.Branches.Add(branch);
        await _context.SaveChangesAsync(cancellationToken);

        // Every branch must have at least one warehouse to operate in Inventory/Sales later.
        _context.Warehouses.Add(new Warehouse
        {
            BranchId = branch.Id,
            Code = $"{branch.Code}-WH",
            Name = $"مخزن {branch.Name}",
            IsDefault = true,
            IsActive = true
        });
        await _context.SaveChangesAsync(cancellationToken);

        return Result<BranchDto>.Success((await GetByIdAsync(branch.Id, cancellationToken))!);
    }

    public async Task<Result<BranchDto>> UpdateAsync(BranchUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<BranchDto>.Failure("معرّف الفرع مطلوب.");

        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == dto.Id, cancellationToken);
        if (branch is null) return Result<BranchDto>.Failure("الفرع غير موجود.");

        var validation = await ValidateAsync(dto, cancellationToken);
        if (validation is not null) return Result<BranchDto>.Failure(validation);

        branch.Code = dto.Code.Trim().ToUpperInvariant();
        branch.Name = dto.Name.Trim();
        branch.Type = dto.Type;
        branch.Address = dto.Address;
        branch.Phone = dto.Phone;
        branch.TaxRegistrationNumber = dto.TaxRegistrationNumber;
        branch.LicenseNumber = dto.LicenseNumber;
        branch.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<BranchDto>.Success((await GetByIdAsync(branch.Id, cancellationToken))!);
    }

    public async Task<Result> SetActiveStatusAsync(int branchId, bool isActive, CancellationToken cancellationToken = default)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
        if (branch is null) return Result.Failure("الفرع غير موجود.");

        if (!isActive && branch.IsMainBranch)
            return Result.Failure("لا يمكن تعطيل الفرع الرئيسي.");

        branch.IsActive = isActive;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var branch = await _context.Branches
            .Include(b => b.UserBranches)
            .FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);

        if (branch is null) return Result.Failure("الفرع غير موجود.");
        if (branch.IsMainBranch) return Result.Failure("لا يمكن حذف الفرع الرئيسي.");

        var hasUsers = branch.UserBranches.Any() ||
            await _context.Users.AnyAsync(u => u.DefaultBranchId == branchId, cancellationToken);
        if (hasUsers) return Result.Failure("لا يمكن حذف فرع مرتبط بمستخدمين. قم بإعادة تعيين المستخدمين أولاً.");

        // Soft delete handled automatically by AuditableEntitySaveChangesInterceptor.
        _context.Branches.Remove(branch);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<List<WarehouseDto>> GetWarehousesAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var warehouses = await _context.Warehouses
            .Include(w => w.Branch)
            .Where(w => w.BranchId == branchId)
            .OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name)
            .ToListAsync(cancellationToken);

        return warehouses.Select(MapWarehouseToDto).ToList();
    }

    public async Task<Result<WarehouseDto>> CreateWarehouseAsync(WarehouseUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var branchExists = await _context.Branches.AnyAsync(b => b.Id == dto.BranchId, cancellationToken);
        if (!branchExists) return Result<WarehouseDto>.Failure("الفرع غير موجود.");

        var codeTaken = await _context.Warehouses.AnyAsync(w => w.BranchId == dto.BranchId && w.Code == dto.Code, cancellationToken);
        if (codeTaken) return Result<WarehouseDto>.Failure("رمز المخزن مستخدم مسبقاً ضمن هذا الفرع.");

        if (dto.IsDefault)
            await ClearOtherDefaultsAsync(dto.BranchId, null, cancellationToken);

        var warehouse = new Warehouse
        {
            BranchId = dto.BranchId,
            Code = dto.Code.Trim().ToUpperInvariant(),
            Name = dto.Name.Trim(),
            IsDefault = dto.IsDefault,
            IsActive = dto.IsActive
        };

        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.Warehouses.Include(w => w.Branch).FirstAsync(w => w.Id == warehouse.Id, cancellationToken);
        return Result<WarehouseDto>.Success(MapWarehouseToDto(reloaded));
    }

    public async Task<Result<WarehouseDto>> UpdateWarehouseAsync(WarehouseUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<WarehouseDto>.Failure("معرّف المخزن مطلوب.");

        var warehouse = await _context.Warehouses.Include(w => w.Branch).FirstOrDefaultAsync(w => w.Id == dto.Id, cancellationToken);
        if (warehouse is null) return Result<WarehouseDto>.Failure("المخزن غير موجود.");

        var codeTaken = await _context.Warehouses
            .AnyAsync(w => w.BranchId == dto.BranchId && w.Code == dto.Code && w.Id != dto.Id, cancellationToken);
        if (codeTaken) return Result<WarehouseDto>.Failure("رمز المخزن مستخدم مسبقاً ضمن هذا الفرع.");

        if (dto.IsDefault && !warehouse.IsDefault)
            await ClearOtherDefaultsAsync(dto.BranchId, warehouse.Id, cancellationToken);

        if (!dto.IsDefault && warehouse.IsDefault)
        {
            var hasOtherDefault = await _context.Warehouses.AnyAsync(w => w.BranchId == dto.BranchId && w.Id != warehouse.Id, cancellationToken);
            if (!hasOtherDefault)
                return Result<WarehouseDto>.Failure("يجب أن يحتفظ الفرع بمخزن افتراضي واحد على الأقل.");
        }

        warehouse.Code = dto.Code.Trim().ToUpperInvariant();
        warehouse.Name = dto.Name.Trim();
        warehouse.IsDefault = dto.IsDefault;
        warehouse.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        return Result<WarehouseDto>.Success(MapWarehouseToDto(warehouse));
    }

    public async Task<Result> DeleteWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken);
        if (warehouse is null) return Result.Failure("المخزن غير موجود.");

        if (warehouse.IsDefault)
            return Result.Failure("لا يمكن حذف المخزن الافتراضي للفرع. عيّن مخزناً آخر كافتراضي أولاً.");

        _context.Warehouses.Remove(warehouse);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task ClearOtherDefaultsAsync(int branchId, int? excludeWarehouseId, CancellationToken cancellationToken)
    {
        var others = await _context.Warehouses
            .Where(w => w.BranchId == branchId && w.Id != excludeWarehouseId && w.IsDefault)
            .ToListAsync(cancellationToken);

        foreach (var w in others) w.IsDefault = false;
    }

    private async Task<string?> ValidateAsync(BranchUpsertDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) return "رمز الفرع مطلوب.";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "اسم الفرع مطلوب.";

        var codeTaken = await _context.Branches
            .AnyAsync(b => b.Code == dto.Code.Trim().ToUpper() && b.Id != dto.Id, cancellationToken);
        if (codeTaken) return "رمز الفرع مستخدم مسبقاً.";

        return null;
    }

    private static BranchDto MapToDto(Branch b) => new()
    {
        Id = b.Id,
        Code = b.Code,
        Name = b.Name,
        Type = b.Type,
        Address = b.Address,
        Phone = b.Phone,
        TaxRegistrationNumber = b.TaxRegistrationNumber,
        LicenseNumber = b.LicenseNumber,
        IsActive = b.IsActive,
        IsMainBranch = b.IsMainBranch,
        WarehouseCount = b.Warehouses.Count,
        UserCount = b.UserBranches.Count
    };

    private static WarehouseDto MapWarehouseToDto(Warehouse w) => new()
    {
        Id = w.Id,
        BranchId = w.BranchId,
        BranchName = w.Branch.Name,
        Code = w.Code,
        Name = w.Name,
        IsDefault = w.IsDefault,
        IsActive = w.IsActive
    };
}
