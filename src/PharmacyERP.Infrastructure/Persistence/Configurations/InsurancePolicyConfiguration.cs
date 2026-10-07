using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class InsurancePolicyConfiguration : IEntityTypeConfiguration<InsurancePolicy>
{
    public void Configure(EntityTypeBuilder<InsurancePolicy> builder)
    {
        builder.ToTable("InsurancePolicies");

        builder.Property(p => p.PolicyNumber).IsRequired().HasMaxLength(50);
        builder.Property(p => p.CoveragePercent).HasColumnType("decimal(5,2)");

        builder.HasIndex(p => new { p.InsuranceCompanyId, p.PolicyNumber }).IsUnique();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasOne(p => p.Customer)
            .WithMany()
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.InsuranceCompany)
            .WithMany(c => c.Policies)
            .HasForeignKey(p => p.InsuranceCompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
