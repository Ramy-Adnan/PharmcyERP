using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> builder)
    {
        builder.ToTable("ItemCategories");
        builder.Property(c => c.Code).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
