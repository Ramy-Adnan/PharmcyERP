using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class SalesInvoiceItemConfiguration : IEntityTypeConfiguration<SalesInvoiceItem>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceItem> builder)
    {
        builder.HasOne<ItemSaleUnit>().WithMany().HasForeignKey(x => x.ItemSaleUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("SalesInvoiceItems");

        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(i => i.TaxRatePercent).HasColumnType("decimal(5,2)");
        builder.Property(i => i.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.LineTotal).HasColumnType("decimal(18,2)");
        builder.Ignore(i => i.QuantityReturnable);
        builder.Property(i => i.UnitsPerSale).HasDefaultValue(1);
        builder.Property(i => i.UnitName).IsRequired().HasMaxLength(50);
        builder.Property(i => i.ItemDisplayName).HasMaxLength(500);

        builder.HasOne(i => i.SalesInvoice)
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Item).WithMany().HasForeignKey(i => i.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
