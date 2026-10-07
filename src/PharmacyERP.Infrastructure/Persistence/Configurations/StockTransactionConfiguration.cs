using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class StockTransactionConfiguration : IEntityTypeConfiguration<StockTransaction>
{
    public void Configure(EntityTypeBuilder<StockTransaction> builder)
    {
        builder.ToTable("StockTransactions");

        builder.Property(t => t.ReferenceType).HasMaxLength(50);
        builder.Property(t => t.Notes).HasMaxLength(500);

        builder.HasIndex(t => t.TransactionAtUtc);
        builder.HasIndex(t => new { t.BatchId, t.TransactionAtUtc });

        builder.HasOne(t => t.Item)
            .WithMany(i => i.StockTransactions)
            .HasForeignKey(t => t.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Batch)
            .WithMany(b => b.StockTransactions)
            .HasForeignKey(t => t.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Warehouse)
            .WithMany()
            .HasForeignKey(t => t.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
