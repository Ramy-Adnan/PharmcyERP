using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.Property(u => u.FullName).IsRequired().HasMaxLength(150);
        builder.Property(u => u.Username).IsRequired().HasMaxLength(50);
        builder.Property(u => u.Email).HasMaxLength(150);
        builder.Property(u => u.PhoneNumber).HasMaxLength(30);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(300);

        builder.HasIndex(u => u.Username).IsUnique();
        builder.HasIndex(u => u.Email);

        builder.Property(u => u.RowVersion).IsRowVersion();

        builder.HasOne(u => u.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.DefaultBranch)
            .WithMany()
            .HasForeignKey(u => u.DefaultBranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}
