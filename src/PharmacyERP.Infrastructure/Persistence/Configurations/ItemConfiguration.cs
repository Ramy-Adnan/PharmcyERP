using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Items");

        builder.Property(i => i.Code).IsRequired().HasMaxLength(30);
        builder.Property(i => i.Barcode).HasMaxLength(50);
        builder.Property(i => i.Name).IsRequired().HasMaxLength(250);
        builder.Property(i => i.GenericName).HasMaxLength(250);
        builder.Property(i => i.Strength).HasMaxLength(50);

        builder.Property(i => i.DefaultSalePrice).HasColumnType("decimal(18,2)");
        builder.Property(i => i.DefaultPurchasePrice).HasColumnType("decimal(18,2)");
        builder.Property(i => i.TaxRatePercent).HasColumnType("decimal(5,2)");

        builder.HasIndex(i => i.Code).IsUnique();
        builder.HasIndex(i => i.Barcode);
        builder.HasIndex(i => i.Name);

        builder.Property(i => i.RowVersion).IsRowVersion();

        builder.HasOne(i => i.Category)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.UnitOfMeasure)
            .WithMany(u => u.Items)
            .HasForeignKey(i => i.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Manufacturer)
            .WithMany(m => m.Items)
            .HasForeignKey(i => i.ManufacturerId)
            .OnDelete(DeleteBehavior.Restrict);

        // No soft-delete query filter here, deliberately. Items are never deleted anywhere in
        // the system — they are retired via IsActive — so the filter could never evaluate to
        // false, yet it made Item the filtered principal of required relationships with
        // SalesInvoiceItem / PurchaseInvoiceItem / SalesReturnItem. That risked historical
        // invoice lines silently disappearing, which is unacceptable for pharmacy audit records.
    }
}
