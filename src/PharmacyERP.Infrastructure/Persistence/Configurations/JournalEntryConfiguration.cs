using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries");

        builder.Property(j => j.Number).IsRequired().HasMaxLength(30);
        builder.Property(j => j.Description).IsRequired().HasMaxLength(500);
        builder.Property(j => j.ReferenceType).HasMaxLength(50);

        builder.HasIndex(j => j.Number).IsUnique();
        builder.HasIndex(j => j.EntryDate);
        builder.HasIndex(j => new { j.ReferenceType, j.ReferenceId });
        builder.Property(j => j.RowVersion).IsRowVersion();

        builder.HasOne(j => j.Branch).WithMany().HasForeignKey(j => j.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(j => !j.IsDeleted);
    }
}
