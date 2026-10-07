using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.ToTable("SalesReturns");

        builder.Property(r => r.Number).IsRequired().HasMaxLength(30);
        builder.Property(r => r.Reason).IsRequired().HasMaxLength(500);
        builder.Property(r => r.TotalAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(r => r.Number).IsUnique();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasOne(r => r.SalesInvoice)
            .WithMany(s => s.Returns)
            .HasForeignKey(r => r.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Branch).WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.ProcessedByUser).WithMany().HasForeignKey(r => r.ProcessedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
