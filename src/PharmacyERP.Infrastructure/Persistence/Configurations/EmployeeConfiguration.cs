using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");

        builder.Property(e => e.EmployeeCode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(150);
        builder.Property(e => e.NationalId).HasMaxLength(30);
        builder.Property(e => e.Phone).HasMaxLength(30);
        builder.Property(e => e.Address).HasMaxLength(300);
        builder.Property(e => e.JobTitle).IsRequired().HasMaxLength(100);
        builder.Property(e => e.MonthlyBaseSalary).HasColumnType("decimal(12,2)");

        builder.HasIndex(e => e.EmployeeCode).IsUnique();
        builder.HasIndex(e => e.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");

        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Shift).WithMany(s => s.Employees).HasForeignKey(e => e.ShiftId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
