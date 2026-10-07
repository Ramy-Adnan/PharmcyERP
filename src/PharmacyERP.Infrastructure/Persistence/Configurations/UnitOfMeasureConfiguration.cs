using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("UnitsOfMeasure");
        builder.Property(u => u.Code).IsRequired().HasMaxLength(20);
        builder.Property(u => u.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(u => u.Code).IsUnique();
        builder.Property(u => u.RowVersion).IsRowVersion();
        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}
