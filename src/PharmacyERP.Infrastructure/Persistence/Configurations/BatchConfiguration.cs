using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("Batches");

        builder.Property(b => b.BatchNumber).IsRequired().HasMaxLength(60);
        builder.Property(b => b.PurchasePrice).HasColumnType("decimal(22,6)");
        builder.Property(b => b.PackageSalePrice).HasColumnType("decimal(18,2)");
        builder.Property(b => b.SalePriceOverride).HasColumnType("decimal(18,2)");
        builder.Property(b => b.SupplierReference).HasMaxLength(100);

        builder.HasIndex(b => new { b.ItemId, b.WarehouseId, b.ExpiryDate });
        builder.HasIndex(b => b.ExpiryDate);

        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasOne(b => b.Item)
            .WithMany(i => i.Batches)
            .HasForeignKey(b => b.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Warehouse)
            .WithMany()
            .HasForeignKey(b => b.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // No soft-delete query filter here, for the same reason as Item: batches are never
        // deleted, and filtering them would have put historical StockTransaction and
        // SalesInvoiceItemBatch rows at risk of vanishing from the ledger.
    }
}
