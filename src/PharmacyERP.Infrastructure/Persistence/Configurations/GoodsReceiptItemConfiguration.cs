using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class GoodsReceiptItemConfiguration : IEntityTypeConfiguration<GoodsReceiptItem>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptItem> builder)
    {
        builder.ToTable("GoodsReceiptItems");

        builder.Property(i => i.BatchNumber).IsRequired().HasMaxLength(50);
        builder.Property(i => i.UnitCost).HasColumnType("decimal(18,2)");
        builder.Property(i => i.SalePrice).HasColumnType("decimal(18,2)");

        builder.HasOne(i => i.GoodsReceiptNote)
            .WithMany(g => g.Items)
            .HasForeignKey(i => i.GoodsReceiptNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.PurchaseOrderItem)
            .WithMany()
            .HasForeignKey(i => i.PurchaseOrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Item).WithMany().HasForeignKey(i => i.ItemId).OnDelete(DeleteBehavior.Restrict);

        // GoodsReceiptItem is a BaseEntity, so it mirrors its principal's filter: deleting a
        // draft goods-receipt note must take its lines out of every query with it.
        builder.HasQueryFilter(i => !i.GoodsReceiptNote.IsDeleted);
    }
}
