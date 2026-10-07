using Microsoft.EntityFrameworkCore;
using PharmacyERP.Infrastructure.Persistence;
using PharmacyERP.Infrastructure.Persistence.Interceptors;

namespace PharmacyERP.Application.Tests.Common;

/// <summary>Keep the production relationships/constraints; translate SQL Server-only storage types for SQLite.</summary>
public sealed class SqliteTestDb : ApplicationDbContext
{
    public SqliteTestDb(DbContextOptions<ApplicationDbContext> options, AuditableEntitySaveChangesInterceptor interceptor)
        : base(options, interceptor) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.ClrType == typeof(string)) property.SetColumnType("TEXT");
    }
}
