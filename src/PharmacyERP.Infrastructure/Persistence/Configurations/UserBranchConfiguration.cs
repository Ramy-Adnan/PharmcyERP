using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder)
    {
        builder.ToTable("UserBranches");

        builder.HasIndex(ub => new { ub.UserId, ub.BranchId }).IsUnique();

        builder.HasOne(ub => ub.User)
            .WithMany(u => u.UserBranches)
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ub => ub.Branch)
            .WithMany(b => b.UserBranches)
            .HasForeignKey(ub => ub.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mirrors the Branch filter: access grants to a deleted branch must not be returned.
        builder.HasQueryFilter(ub => !ub.Branch.IsDeleted);
    }
}
