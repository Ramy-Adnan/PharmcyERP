using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class GoodsReceiptNoteConfiguration : IEntityTypeConfiguration<GoodsReceiptNote>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptNote> builder)
    {
        builder.ToTable("GoodsReceiptNotes");

        builder.Property(g => g.Number).IsRequired().HasMaxLength(30);
        builder.Property(g => g.Notes).HasMaxLength(1000);

        builder.HasIndex(g => g.Number).IsUnique();
        builder.Property(g => g.RowVersion).IsRowVersion();

        builder.HasOne(g => g.PurchaseOrder)
            .WithMany(p => p.GoodsReceiptNotes)
            .HasForeignKey(g => g.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.Supplier).WithMany().HasForeignKey(g => g.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.Branch).WithMany().HasForeignKey(g => g.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.Warehouse).WithMany().HasForeignKey(g => g.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(g => !g.IsDeleted);
    }
}
