using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFlexibleUnitsAndInvoiceImageImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ItemSaleUnitId",
                table: "SalesInvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemSaleUnitId",
                table: "PurchaseOrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BonusQuantity",
                table: "PurchaseInvoiceItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ItemSaleUnitId",
                table: "PurchaseInvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BonusQuantity",
                table: "GoodsReceiptItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ItemSaleUnitId",
                table: "GoodsReceiptItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemSaleUnitId",
                table: "Batches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceivedUnitFactor",
                table: "Batches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemSaleUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BaseUnitCount = table.Column<int>(type: "int", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemSaleUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemSaleUnits_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseImageImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    GoodsReceiptNoteId = table.Column<int>(type: "int", nullable: false),
                    SupplierInvoiceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ParsedTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ReviewedTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseImageImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseImageImports_GoodsReceiptNotes_GoodsReceiptNoteId",
                        column: x => x.GoodsReceiptNoteId,
                        principalTable: "GoodsReceiptNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseImageImports_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseImageImports_Users_ImportedByUserId",
                        column: x => x.ImportedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierItemAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierItemAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierItemAliases_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierItemAliases_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_ItemSaleUnitId",
                table: "SalesInvoiceItems",
                column: "ItemSaleUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderItems_ItemSaleUnitId",
                table: "PurchaseOrderItems",
                column: "ItemSaleUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_ItemSaleUnitId",
                table: "PurchaseInvoiceItems",
                column: "ItemSaleUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_ItemSaleUnitId",
                table: "GoodsReceiptItems",
                column: "ItemSaleUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ItemSaleUnitId",
                table: "Batches",
                column: "ItemSaleUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemSaleUnits_Barcode",
                table: "ItemSaleUnits",
                column: "Barcode");

            migrationBuilder.CreateIndex(
                name: "IX_ItemSaleUnits_ItemId_Name",
                table: "ItemSaleUnits",
                columns: new[] { "ItemId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseImageImports_GoodsReceiptNoteId",
                table: "PurchaseImageImports",
                column: "GoodsReceiptNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseImageImports_ImportedByUserId",
                table: "PurchaseImageImports",
                column: "ImportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseImageImports_RequestId",
                table: "PurchaseImageImports",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseImageImports_SourceHash",
                table: "PurchaseImageImports",
                column: "SourceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseImageImports_SupplierId_SupplierInvoiceKey",
                table: "PurchaseImageImports",
                columns: new[] { "SupplierId", "SupplierInvoiceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierItemAliases_ItemId",
                table: "SupplierItemAliases",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierItemAliases_SupplierId_NormalizedName",
                table: "SupplierItemAliases",
                columns: new[] { "SupplierId", "NormalizedName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Batches_ItemSaleUnits_ItemSaleUnitId",
                table: "Batches",
                column: "ItemSaleUnitId",
                principalTable: "ItemSaleUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptItems_ItemSaleUnits_ItemSaleUnitId",
                table: "GoodsReceiptItems",
                column: "ItemSaleUnitId",
                principalTable: "ItemSaleUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceItems_ItemSaleUnits_ItemSaleUnitId",
                table: "PurchaseInvoiceItems",
                column: "ItemSaleUnitId",
                principalTable: "ItemSaleUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderItems_ItemSaleUnits_ItemSaleUnitId",
                table: "PurchaseOrderItems",
                column: "ItemSaleUnitId",
                principalTable: "ItemSaleUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceItems_ItemSaleUnits_ItemSaleUnitId",
                table: "SalesInvoiceItems",
                column: "ItemSaleUnitId",
                principalTable: "ItemSaleUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Batches_ItemSaleUnits_ItemSaleUnitId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptItems_ItemSaleUnits_ItemSaleUnitId",
                table: "GoodsReceiptItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceItems_ItemSaleUnits_ItemSaleUnitId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderItems_ItemSaleUnits_ItemSaleUnitId",
                table: "PurchaseOrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceItems_ItemSaleUnits_ItemSaleUnitId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropTable(
                name: "ItemSaleUnits");

            migrationBuilder.DropTable(
                name: "PurchaseImageImports");

            migrationBuilder.DropTable(
                name: "SupplierItemAliases");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItems_ItemSaleUnitId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderItems_ItemSaleUnitId",
                table: "PurchaseOrderItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceItems_ItemSaleUnitId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptItems_ItemSaleUnitId",
                table: "GoodsReceiptItems");

            migrationBuilder.DropIndex(
                name: "IX_Batches_ItemSaleUnitId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "ItemSaleUnitId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "ItemSaleUnitId",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "BonusQuantity",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "ItemSaleUnitId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "BonusQuantity",
                table: "GoodsReceiptItems");

            migrationBuilder.DropColumn(
                name: "ItemSaleUnitId",
                table: "GoodsReceiptItems");

            migrationBuilder.DropColumn(
                name: "ItemSaleUnitId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "ReceivedUnitFactor",
                table: "Batches");
        }
    }
}
