using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class InsuranceClaimConfiguration : IEntityTypeConfiguration<InsuranceClaim>
{
    public void Configure(EntityTypeBuilder<InsuranceClaim> builder)
    {
        builder.ToTable("InsuranceClaims");

        builder.Property(c => c.ClaimNumber).IsRequired().HasMaxLength(30);
        builder.Property(c => c.Notes).HasMaxLength(500);
        builder.HasIndex(c => c.ClaimNumber).IsUnique();

        builder.Property(c => c.ClaimedAmount).HasColumnType("decimal(18,2)");
        builder.Property(c => c.ApprovedAmount).HasColumnType("decimal(18,2)");

        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasOne(c => c.SalesInvoice)
            .WithMany()
            .HasForeignKey(c => c.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.InsurancePolicy)
            .WithMany(p => p.Claims)
            .HasForeignKey(c => c.InsurancePolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
