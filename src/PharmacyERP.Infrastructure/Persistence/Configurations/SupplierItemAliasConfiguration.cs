using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;
namespace PharmacyERP.Infrastructure.Persistence.Configurations;
public class SupplierItemAliasConfiguration : IEntityTypeConfiguration<SupplierItemAlias>
{
    public void Configure(EntityTypeBuilder<SupplierItemAlias> builder)
    {
        builder.ToTable("SupplierItemAliases");
        builder.Property(a => a.SourceName).IsRequired().HasMaxLength(250);
        builder.Property(a => a.NormalizedName).IsRequired().HasMaxLength(400);
        builder.HasIndex(a => new { a.SupplierId, a.NormalizedName }).IsUnique();
        builder.HasOne<Supplier>().WithMany().HasForeignKey(a => a.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Item>().WithMany().HasForeignKey(a => a.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
