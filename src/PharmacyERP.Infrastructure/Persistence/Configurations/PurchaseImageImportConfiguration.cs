using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;
namespace PharmacyERP.Infrastructure.Persistence.Configurations;
public class PurchaseImageImportConfiguration : IEntityTypeConfiguration<PurchaseImageImport>
{
    public void Configure(EntityTypeBuilder<PurchaseImageImport> builder)
    {
        builder.ToTable("PurchaseImageImports");
        builder.Property(i => i.SourceHash).IsRequired().HasMaxLength(64);
        builder.Property(i => i.SupplierInvoiceKey).IsRequired().HasMaxLength(100);
        builder.Property(i => i.ParsedTotal).HasColumnType("decimal(18,2)");
        builder.Property(i => i.ReviewedTotal).HasColumnType("decimal(18,2)");
        builder.HasIndex(i => i.RequestId).IsUnique();
        builder.HasIndex(i => i.SourceHash).IsUnique();
        builder.HasIndex(i => new { i.SupplierId, i.SupplierInvoiceKey }).IsUnique();
        builder.HasOne<Supplier>().WithMany().HasForeignKey(i => i.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptNote>().WithMany().HasForeignKey(i => i.GoodsReceiptNoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.ImportedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
