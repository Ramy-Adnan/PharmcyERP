using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmacyERP.Domain.Entities;

namespace PharmacyERP.Infrastructure.Persistence.Configurations;

public class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        builder.ToTable("SalesInvoices");

        builder.Property(s => s.Number).IsRequired().HasMaxLength(30);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        builder.Property(s => s.SubTotal).HasColumnType("decimal(18,2)");
        builder.Property(s => s.TaxAmount).HasColumnType("decimal(18,2)");
        builder.Property(s => s.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(s => s.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(s => s.AmountTendered).HasColumnType("decimal(18,2)");
        builder.Property(s => s.ChangeGiven).HasColumnType("decimal(18,2)");

        builder.HasIndex(s => s.Number).IsUnique();
        builder.HasIndex(s => s.SaleAtUtc);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasOne(s => s.Branch).WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Warehouse).WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Customer).WithMany(c => c.SalesInvoices).HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.CashierUser).WithMany().HasForeignKey(s => s.CashierUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Prescription).WithMany(p => p.SalesInvoices).HasForeignKey(s => s.PrescriptionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
