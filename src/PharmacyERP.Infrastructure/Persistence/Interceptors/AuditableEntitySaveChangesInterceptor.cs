using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Domain.Common;

namespace PharmacyERP.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Automatically stamps CreatedAtUtc/CreatedByUserId on insert and
/// ModifiedAtUtc/ModifiedByUserId on update for every AuditableEntity,
/// and converts hard deletes into soft deletes. This runs for every
/// SaveChanges call across the whole application — no feature code ever
/// needs to remember to set these fields manually.
/// </summary>
public class AuditableEntitySaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTime _dateTime;

    public AuditableEntitySaveChangesInterceptor(ICurrentUserService currentUserService, IDateTime dateTime)
    {
        _currentUserService = currentUserService;
        _dateTime = dateTime;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateAuditFields(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = _dateTime.UtcNow;
                    entry.Entity.CreatedByUserId = _currentUserService.UserId;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = _dateTime.UtcNow;
                    entry.Entity.ModifiedByUserId = _currentUserService.UserId;
                    break;

                case EntityState.Deleted:
                    // Soft delete: never physically remove pharmacy records.
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAtUtc = _dateTime.UtcNow;
                    entry.Entity.DeletedByUserId = _currentUserService.UserId;
                    break;
            }
        }
    }
}
