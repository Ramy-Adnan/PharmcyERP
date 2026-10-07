using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class PrescriptionItemConfiguration : IEntityTypeConfiguration<PrescriptionItem>
{
    public void Configure(EntityTypeBuilder<PrescriptionItem> builder)
    {
        builder.ToTable("PrescriptionItems");

        builder.Property(pi => pi.DosageInstructions).HasMaxLength(300);
        builder.Ignore(pi => pi.QuantityRemaining);

        builder.Property(pi => pi.RowVersion).IsRowVersion();

        builder.HasOne(pi => pi.Prescription)
            .WithMany(p => p.Items)
            .HasForeignKey(pi => pi.PrescriptionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pi => pi.Item)
            .WithMany()
            .HasForeignKey(pi => pi.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(pi => !pi.IsDeleted);
    }
}
