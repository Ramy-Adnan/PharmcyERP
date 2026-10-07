using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class InsuranceCompanyConfiguration : IEntityTypeConfiguration<InsuranceCompany>
{
    public void Configure(EntityTypeBuilder<InsuranceCompany> builder)
    {
        builder.ToTable("InsuranceCompanies");

        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.Property(c => c.ContactPhone).HasMaxLength(30);
        builder.Property(c => c.ContactEmail).HasMaxLength(150);

        builder.HasIndex(c => c.Name).IsUnique();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
