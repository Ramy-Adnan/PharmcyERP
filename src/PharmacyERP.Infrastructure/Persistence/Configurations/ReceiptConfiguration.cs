using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("Receipts");

        builder.Property(r => r.Number).IsRequired().HasMaxLength(30);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.PayerName).IsRequired().HasMaxLength(150);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.Property(r => r.ReferenceType).HasMaxLength(50);

        builder.HasIndex(r => r.Number).IsUnique();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasOne(r => r.Branch).WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.CashBox).WithMany().HasForeignKey(r => r.CashBoxId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.BankAccount).WithMany().HasForeignKey(r => r.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.JournalEntry).WithMany().HasForeignKey(r => r.JournalEntryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
