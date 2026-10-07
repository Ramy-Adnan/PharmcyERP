using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> builder)
    {
        builder.ToTable("PurchaseInvoices");

        builder.Property(p => p.Number).IsRequired().HasMaxLength(30);
        builder.Property(p => p.Notes).HasMaxLength(1000);

        builder.Property(p => p.SubTotal).HasColumnType("decimal(18,2)");
        builder.Property(p => p.TaxAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.AmountPaid).HasColumnType("decimal(18,2)");
        builder.Ignore(p => p.AmountDue);

        builder.HasIndex(p => p.Number).IsUnique();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.PurchaseInvoices)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.GoodsReceiptNote).WithMany().HasForeignKey(p => p.GoodsReceiptNoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Branch).WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
