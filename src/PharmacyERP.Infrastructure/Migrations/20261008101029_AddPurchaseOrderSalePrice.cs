using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderSalePrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SalePrice",
                table: "PurchaseOrderItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasConfiguredSalePrice",
                table: "Batches",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SalePrice",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "HasConfiguredSalePrice",
                table: "Batches");
        }
    }
}
