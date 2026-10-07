using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("BankAccounts");

        builder.Property(b => b.Name).IsRequired().HasMaxLength(100);
        builder.Property(b => b.BankName).IsRequired().HasMaxLength(100);
        builder.Property(b => b.AccountNumber).IsRequired().HasMaxLength(50);
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasOne(b => b.Account).WithMany().HasForeignKey(b => b.AccountId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}
