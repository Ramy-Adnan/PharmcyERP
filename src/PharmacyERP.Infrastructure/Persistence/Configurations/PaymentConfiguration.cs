using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.Property(p => p.Number).IsRequired().HasMaxLength(30);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayeeName).IsRequired().HasMaxLength(150);
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.Property(p => p.ReferenceType).HasMaxLength(50);

        builder.HasIndex(p => p.Number).IsUnique();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasOne(p => p.Branch).WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.CashBox).WithMany().HasForeignKey(p => p.CashBoxId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.BankAccount).WithMany().HasForeignKey(p => p.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.JournalEntry).WithMany().HasForeignKey(p => p.JournalEntryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
