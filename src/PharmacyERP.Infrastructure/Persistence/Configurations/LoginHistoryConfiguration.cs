using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class LoginHistoryConfiguration : IEntityTypeConfiguration<LoginHistory>
{
    public void Configure(EntityTypeBuilder<LoginHistory> builder)
    {
        builder.ToTable("LoginHistories");

        builder.Property(l => l.UsernameAttempted).IsRequired().HasMaxLength(50);
        builder.Property(l => l.FailureReason).HasMaxLength(300);
        builder.Property(l => l.MachineName).HasMaxLength(100);
        builder.Property(l => l.IpAddress).HasMaxLength(50);

        builder.HasOne(l => l.User)
            .WithMany(u => u.LoginHistories)
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(l => l.AttemptedAtUtc);
    }
}
