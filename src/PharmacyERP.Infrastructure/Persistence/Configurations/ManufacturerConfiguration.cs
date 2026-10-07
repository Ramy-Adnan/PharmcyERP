using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class ManufacturerConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> builder)
    {
        builder.ToTable("Manufacturers");
        builder.Property(m => m.Name).IsRequired().HasMaxLength(150);
        builder.Property(m => m.Country).HasMaxLength(100);
        builder.HasIndex(m => m.Name).IsUnique();
        builder.Property(m => m.RowVersion).IsRowVersion();
        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
