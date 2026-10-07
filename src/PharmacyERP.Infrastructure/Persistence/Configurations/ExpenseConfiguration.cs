using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");

        builder.Property(e => e.Number).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Amount).HasColumnType("decimal(18,2)");
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasIndex(e => e.Number).IsUnique();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ExpenseCategory).WithMany().HasForeignKey(e => e.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.CashBox).WithMany().HasForeignKey(e => e.CashBoxId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.BankAccount).WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.JournalEntry).WithMany().HasForeignKey(e => e.JournalEntryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
