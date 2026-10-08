using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;
namespace PharmacyERP.Infrastructure.Persistence.Configurations;
public class ItemSaleUnitConfiguration : IEntityTypeConfiguration<ItemSaleUnit>
{
    public void Configure(EntityTypeBuilder<ItemSaleUnit> builder)
    {
        builder.ToTable("ItemSaleUnits");
        builder.Property(u => u.Name).IsRequired().HasMaxLength(50);
        builder.Property(u => u.Barcode).HasMaxLength(50);
        builder.HasIndex(u => new { u.ItemId, u.Name }).IsUnique();
        builder.HasIndex(u => u.Barcode);
        builder.HasOne(u => u.Item).WithMany(i => i.SaleUnits).HasForeignKey(u => u.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
