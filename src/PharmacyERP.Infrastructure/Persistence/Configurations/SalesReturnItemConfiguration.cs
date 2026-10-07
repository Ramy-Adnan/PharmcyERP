using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class SalesReturnItemConfiguration : IEntityTypeConfiguration<SalesReturnItem>
{
    public void Configure(EntityTypeBuilder<SalesReturnItem> builder)
    {
        builder.ToTable("SalesReturnItems");

        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(i => i.LineTotal).HasColumnType("decimal(18,2)");

        builder.HasOne(i => i.SalesReturn)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.SalesReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.SalesInvoiceItem).WithMany(si => si.ReturnItems).HasForeignKey(i => i.SalesInvoiceItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Item).WithMany().HasForeignKey(i => i.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
