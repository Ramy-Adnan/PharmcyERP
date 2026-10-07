using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class PayrollRunLineConfiguration : IEntityTypeConfiguration<PayrollRunLine>
{
    public void Configure(EntityTypeBuilder<PayrollRunLine> builder)
    {
        builder.ToTable("PayrollRunLines");

        builder.Property(l => l.BaseSalary).HasColumnType("decimal(12,2)");
        builder.Property(l => l.TotalCommissions).HasColumnType("decimal(12,2)");
        builder.Property(l => l.Deductions).HasColumnType("decimal(12,2)");
        builder.Property(l => l.DeductionNotes).HasMaxLength(300);
        builder.Ignore(l => l.NetPay);

        builder.HasIndex(l => new { l.PayrollRunId, l.EmployeeId }).IsUnique();
        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.HasOne(l => l.PayrollRun).WithMany(p => p.Lines).HasForeignKey(l => l.PayrollRunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.Employee).WithMany(e => e.PayrollRunLines).HasForeignKey(l => l.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
