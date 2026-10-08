using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddItemPackagingAndSaleUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ItemDisplayName",
                table: "SalesInvoiceItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "SalesInvoiceItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "UnitsPerSale",
                table: "SalesInvoiceItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "SalesInvoiceItemBatches",
                type: "decimal(22,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<string>(
                name: "BaseUnitBarcode",
                table: "Items",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageUnitName",
                table: "Items",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "علبة");

            migrationBuilder.AddColumn<int>(
                name: "UnitsPerPackage",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<decimal>(
                name: "PurchasePrice",
                table: "Batches",
                type: "decimal(22,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<decimal>(
                name: "PackageSalePrice",
                table: "Batches",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_BaseUnitBarcode",
                table: "Items",
                column: "BaseUnitBarcode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_BaseUnitBarcode",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ItemDisplayName",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "UnitName",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "UnitsPerSale",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "BaseUnitBarcode",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PackageUnitName",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "UnitsPerPackage",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PackageSalePrice",
                table: "Batches");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "SalesInvoiceItemBatches",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(22,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "PurchasePrice",
                table: "Batches",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(22,6)");
        }
    }
}
