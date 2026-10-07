using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
        builder.ToTable("Attendances");

        builder.Property(a => a.Notes).HasMaxLength(300);
        builder.HasIndex(a => new { a.EmployeeId, a.AttendanceDate }).IsUnique();

        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasOne(a => a.Employee).WithMany(e => e.AttendanceRecords).HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
