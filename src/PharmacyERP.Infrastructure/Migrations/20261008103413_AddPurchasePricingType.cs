using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasePricingType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseType",
                table: "PurchaseOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseType",
                table: "PurchaseInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseType",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseType",
                table: "GoodsReceiptNotes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseType",
                table: "Batches",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseType",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "PurchaseType",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "PurchaseType",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PurchaseType",
                table: "GoodsReceiptNotes");

            migrationBuilder.DropColumn(
                name: "PurchaseType",
                table: "Batches");
        }
    }
}
