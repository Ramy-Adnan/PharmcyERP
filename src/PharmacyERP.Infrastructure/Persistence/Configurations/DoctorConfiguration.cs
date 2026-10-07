using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("Doctors");

        builder.Property(d => d.FullName).IsRequired().HasMaxLength(150);
        builder.Property(d => d.LicenseNumber).HasMaxLength(50);
        builder.Property(d => d.Specialty).HasMaxLength(100);
        builder.Property(d => d.Phone).HasMaxLength(30);

        builder.Property(d => d.RowVersion).IsRowVersion();
        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
