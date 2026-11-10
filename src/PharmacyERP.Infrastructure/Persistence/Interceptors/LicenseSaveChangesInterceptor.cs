using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmacyERP.Application.Features.Licensing;
using PharmacyERP.Domain.Common;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Final write boundary for all modules, including stock and financial posting.
/// Only authentication bookkeeping/password changes remain writable without a
/// license. Seed data uses an explicit, bounded bootstrap scope at startup.
/// </summary>
public sealed class LicenseSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ILicenseService _licenseService;
    private readonly LicenseRuntimeState _runtime;
    private static readonly HashSet<string> AuthenticationProperties = new(StringComparer.Ordinal)
    {
        nameof(User.PasswordHash), nameof(User.FailedLoginAttempts), nameof(User.LockoutEndUtc), nameof(User.LastLoginAtUtc),
        nameof(AuditableEntity.ModifiedAtUtc), nameof(AuditableEntity.ModifiedByUserId)
    };

    public LicenseSaveChangesInterceptor(ILicenseService licenseService, LicenseRuntimeState runtime)
    {
        _licenseService = licenseService;
        _runtime = runtime;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (!NeedsLicense(eventData.Context)) return result;
        // Avoid blocking an async license implementation on the WPF synchronization context.
        var error = Task.Run(() => LicenseWriteAccess.CheckAsync(_licenseService)).GetAwaiter().GetResult();
        DenyIfNeeded(eventData.Context!, error);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!NeedsLicense(eventData.Context)) return result;
        var error = await LicenseWriteAccess.CheckAsync(_licenseService, cancellationToken).ConfigureAwait(false);
        DenyIfNeeded(eventData.Context!, error);
        return result;
    }

    private bool NeedsLicense(DbContext? context)
    {
        if (context is null || _runtime.IsBootstrapping) return false;
        context.ChangeTracker.DetectChanges();
        return context.ChangeTracker.Entries().Any(entry =>
            (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) && !IsAuthenticationWrite(entry));
    }

    private static bool IsAuthenticationWrite(EntityEntry entry) =>
        entry.Entity is LoginHistory && entry.State == EntityState.Added ||
        entry.Entity is AuditLog && entry.State == EntityState.Added ||
        entry.Entity is User && entry.State == EntityState.Modified &&
            entry.Properties.Where(property => property.IsModified).All(property => AuthenticationProperties.Contains(property.Metadata.Name));

    private static void DenyIfNeeded(DbContext context, string? error)
    {
        if (error is null) return;
        // WPF scopes can outlive a failed operation. Discard denied mutations so
        // a later login/password save cannot accidentally commit old stock edits.
        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.State is EntityState.Modified or EntityState.Deleted).ToList())
            entry.CurrentValues.SetValues(entry.OriginalValues);
        context.ChangeTracker.Clear();
        throw new LicenseWriteDeniedException(error);
    }
}
