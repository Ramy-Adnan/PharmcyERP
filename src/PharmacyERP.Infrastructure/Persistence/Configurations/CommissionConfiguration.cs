using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class CommissionConfiguration : IEntityTypeConfiguration<Commission>
{
    public void Configure(EntityTypeBuilder<Commission> builder)
    {
        builder.ToTable("Commissions");

        builder.Property(c => c.Amount).HasColumnType("decimal(12,2)");
        builder.Property(c => c.Notes).HasMaxLength(300);

        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasOne(c => c.Employee).WithMany(e => e.Commissions).HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.SalesInvoice).WithMany().HasForeignKey(c => c.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.PayrollRunLine).WithMany(l => l.Commissions).HasForeignKey(c => c.PayrollRunLineId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
