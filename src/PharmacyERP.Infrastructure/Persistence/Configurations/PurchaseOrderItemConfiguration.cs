using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        builder.ToTable("PurchaseOrderItems");

        builder.Property(i => i.UnitCost).HasColumnType("decimal(18,2)");
        builder.Property(i => i.SalePrice).HasColumnType("decimal(18,2)");
        builder.Property(i => i.TaxRatePercent).HasColumnType("decimal(5,2)");
        builder.Ignore(i => i.QuantityOutstanding);
        builder.Ignore(i => i.LineTotal);

        builder.HasOne(i => i.PurchaseOrder)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Item).WithMany().HasForeignKey(i => i.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
