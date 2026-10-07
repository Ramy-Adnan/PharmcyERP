using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");

        builder.Property(b => b.Code).IsRequired().HasMaxLength(20);
        builder.Property(b => b.Name).IsRequired().HasMaxLength(150);
        builder.Property(b => b.Address).HasMaxLength(300);
        builder.Property(b => b.Phone).HasMaxLength(30);
        builder.Property(b => b.TaxRegistrationNumber).HasMaxLength(50);
        builder.Property(b => b.LicenseNumber).HasMaxLength(50);

        builder.HasIndex(b => b.Code).IsUnique();
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}
