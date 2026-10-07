using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");

        builder.Property(s => s.Code).IsRequired().HasMaxLength(20);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.ContactPerson).HasMaxLength(150);
        builder.Property(s => s.Phone).HasMaxLength(30);
        builder.Property(s => s.Email).HasMaxLength(150);
        builder.Property(s => s.Address).HasMaxLength(300);
        builder.Property(s => s.TaxRegistrationNumber).HasMaxLength(50);

        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
