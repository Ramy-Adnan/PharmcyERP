using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Branches.DTOs;

namespace PharmacyERP.Application.Features.Branches;

/// <summary>
/// Full CRUD + business rules for Branches and their Warehouses. Enforces
/// invariants that span both entities (e.g. a branch cannot be deactivated
/// while it still has active users, and every branch must always retain
/// exactly one default warehouse) which is why both concerns live in a
/// single service rather than being split per-entity.
/// </summary>
public interface IBranchService
{
    Task<List<BranchDto>> GetAllAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<BranchDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<BranchDto>> CreateAsync(BranchUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<BranchDto>> UpdateAsync(BranchUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SetActiveStatusAsync(int branchId, bool isActive, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(int branchId, CancellationToken cancellationToken = default);

    Task<List<WarehouseDto>> GetWarehousesAsync(int branchId, CancellationToken cancellationToken = default);
    Task<Result<WarehouseDto>> CreateWarehouseAsync(WarehouseUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<WarehouseDto>> UpdateWarehouseAsync(WarehouseUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> DeleteWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default);
}
