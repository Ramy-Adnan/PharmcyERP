using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.Property(a => a.UserName).HasMaxLength(100);
        builder.Property(a => a.EntityName).IsRequired().HasMaxLength(150);
        builder.Property(a => a.EntityId).HasMaxLength(50);
        builder.Property(a => a.IpAddress).HasMaxLength(50);
        builder.Property(a => a.MachineName).HasMaxLength(100);

        builder.Property(a => a.OldValuesJson).HasColumnType("nvarchar(max)");
        builder.Property(a => a.NewValuesJson).HasColumnType("nvarchar(max)");

        builder.HasIndex(a => a.TimestampUtc);
        builder.HasIndex(a => new { a.EntityName, a.EntityId });
    }
}
