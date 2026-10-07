using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class SalesInvoiceItemBatchConfiguration : IEntityTypeConfiguration<SalesInvoiceItemBatch>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceItemBatch> builder)
    {
        builder.ToTable("SalesInvoiceItemBatches");

        builder.Property(b => b.UnitCost).HasColumnType("decimal(18,2)");
        builder.Ignore(b => b.QuantityAvailableToReturn);

        builder.HasOne(b => b.SalesInvoiceItem)
            .WithMany(i => i.BatchAllocations)
            .HasForeignKey(b => b.SalesInvoiceItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Batch).WithMany().HasForeignKey(b => b.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}
