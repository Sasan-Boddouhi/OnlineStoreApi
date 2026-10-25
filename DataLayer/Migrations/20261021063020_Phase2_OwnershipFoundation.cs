using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <summary>
    /// Phase 2 — Ownership Foundation.
    ///
    /// ⚠️ این فایل دستی بازنویسی شده است. اسکافولد اولیهٔ EF عملیات
    /// «RenameColumn Inventory.ProductId -> ListingId» را تولید می‌کرد که از نظر داده غلط است
    /// (مقادیر ProductId در ستون ListingId قرار می‌گرفتند و FK نقض می‌شد).
    /// ترتیب صحیح: افزودن ستون nullable → بک‌فیل → NOT NULL کردن → حذف ستون قدیمی.
    /// </summary>
    public partial class Phase2_OwnershipFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- ۱) برداشتن FK/ایندکس‌های قدیمی ----------
            migrationBuilder.DropForeignKey(
                name: "FK_Inventory_Product_ProductId",
                table: "Inventory");

            migrationBuilder.DropForeignKey(
                name: "FK_Inventory_Warehouse_WarehouseId",
                table: "Inventory");

            migrationBuilder.DropIndex(
                name: "IX_Warehouse_Name",
                table: "Warehouse");

            migrationBuilder.DropIndex(
                name: "IX_Inventory_ProductId_WarehouseId",
                table: "Inventory");

            // ---------- ۲) ستون‌های جدید، همه nullable ----------
            migrationBuilder.AddColumn<int>(
                name: "ListingId",
                table: "Inventory",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellerId",
                table: "Warehouse",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ListingId",
                table: "OrderItem",
                type: "int",
                nullable: true);

            // ---------- ۳) جداول جدید ----------
            migrationBuilder.CreateTable(
                name: "Seller",
                columns: table => new
                {
                    SellerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seller", x => x.SellerId);
                    table.ForeignKey(
                        name: "FK_Seller_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SellerProduct",
                columns: table => new
                {
                    ListingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerProduct", x => x.ListingId);
                    table.ForeignKey(
                        name: "FK_SellerProduct_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SellerProduct_Seller_SellerId",
                        column: x => x.SellerId,
                        principalTable: "Seller",
                        principalColumn: "SellerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Seller_Slug",
                table: "Seller",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Seller_StoreName",
                table: "Seller",
                column: "StoreName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seller_UserId",
                table: "Seller",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerProduct_ProductId",
                table: "SellerProduct",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerProduct_SellerId_ProductId",
                table: "SellerProduct",
                columns: new[] { "SellerId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerProduct_SellerId_Status",
                table: "SellerProduct",
                columns: new[] { "SellerId", "Status" });

            // ---------- ۴) بک‌فیل: سیستم‌فروشنده + Listing + Inventory + OrderItem ----------
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [User]) AND NOT EXISTS (SELECT 1 FROM [Seller])
                BEGIN
                    INSERT INTO [Seller] ([UserId], [StoreName], [Slug], [Status], [IsActive], [CreatedOn], [ModifiedOn])
                    SELECT TOP 1 u.[UserId], N'Platform Store', N'platform', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
                    FROM [User] u
                    ORDER BY u.[UserId];
                END

                DECLARE @sellerId INT = (SELECT TOP 1 [SellerId] FROM [Seller] ORDER BY [SellerId]);

                IF @sellerId IS NOT NULL
                BEGIN
                    INSERT INTO [SellerProduct] ([SellerId], [ProductId], [Price], [Status], [CreatedOn], [ModifiedOn])
                    SELECT @sellerId, p.[ProductId], p.[Price], 1, SYSUTCDATETIME(), SYSUTCDATETIME()
                    FROM [Product] p
                    WHERE NOT EXISTS (
                        SELECT 1 FROM [SellerProduct] sp
                        WHERE sp.[SellerId] = @sellerId AND sp.[ProductId] = p.[ProductId]);

                    UPDATE i
                    SET i.[ListingId] = sp.[ListingId]
                    FROM [Inventory] i
                    JOIN [SellerProduct] sp ON sp.[ProductId] = i.[ProductId]
                    WHERE i.[ListingId] IS NULL;

                    UPDATE oi
                    SET oi.[ListingId] = sp.[ListingId]
                    FROM [OrderItem] oi
                    JOIN [SellerProduct] sp ON sp.[ProductId] = oi.[ProductId]
                    WHERE oi.[ListingId] IS NULL;
                END
            ");

            // ---------- ۵) سخت‌کردن: Inventory.ListingId اجباری می‌شود ----------
            // اگر ردیفی بک‌فیل نشده باشد، شکست صریح بهتر از دادهٔ یتیم است.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [Inventory] WHERE [ListingId] IS NULL)
                    THROW 50000, N'Phase2 backfill: برخی ردیف‌های Inventory به هیچ Listing قابل نگاشت نبودند. قبل از مهاجرت برای هر Product یک Seller/Listing بسازید.', 1;
            ");

            migrationBuilder.AlterColumn<int>(
                name: "ListingId",
                table: "Inventory",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "Inventory");

            // ---------- ۶) ایندکس‌ها و FKهای جدید ----------
            migrationBuilder.CreateIndex(
                name: "IX_Inventory_ListingId_WarehouseId",
                table: "Inventory",
                columns: new[] { "ListingId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_ListingId",
                table: "OrderItem",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_Name",
                table: "Warehouse",
                column: "Name",
                unique: true,
                filter: "[SellerId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_SellerId_Name",
                table: "Warehouse",
                columns: new[] { "SellerId", "Name" },
                unique: true,
                filter: "[SellerId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Inventory_SellerProduct_ListingId",
                table: "Inventory",
                column: "ListingId",
                principalTable: "SellerProduct",
                principalColumn: "ListingId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Inventory_Warehouse_WarehouseId",
                table: "Inventory",
                column: "WarehouseId",
                principalTable: "Warehouse",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItem_SellerProduct_ListingId",
                table: "OrderItem",
                column: "ListingId",
                principalTable: "SellerProduct",
                principalColumn: "ListingId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Warehouse_Seller_SellerId",
                table: "Warehouse",
                column: "SellerId",
                principalTable: "Seller",
                principalColumn: "SellerId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Inventory_SellerProduct_ListingId",
                table: "Inventory");

            migrationBuilder.DropForeignKey(
                name: "FK_Inventory_Warehouse_WarehouseId",
                table: "Inventory");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItem_SellerProduct_ListingId",
                table: "OrderItem");

            migrationBuilder.DropForeignKey(
                name: "FK_Warehouse_Seller_SellerId",
                table: "Warehouse");

            migrationBuilder.DropIndex(
                name: "IX_Inventory_ListingId_WarehouseId",
                table: "Inventory");

            migrationBuilder.DropIndex(
                name: "IX_OrderItem_ListingId",
                table: "OrderItem");

            migrationBuilder.DropIndex(
                name: "IX_Warehouse_Name",
                table: "Warehouse");

            migrationBuilder.DropIndex(
                name: "IX_Warehouse_SellerId_Name",
                table: "Warehouse");

            // بازگرداندن ProductId از روی Listing، پیش از حذف جداول بازار.
            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "Inventory",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE i
                SET i.[ProductId] = sp.[ProductId]
                FROM [Inventory] i
                JOIN [SellerProduct] sp ON sp.[ListingId] = i.[ListingId];
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [Inventory] WHERE [ProductId] IS NULL)
                BEGIN
                    DELETE FROM [Inventory] WHERE [ProductId] IS NULL;
                END
            ");

            migrationBuilder.AlterColumn<int>(
                name: "ProductId",
                table: "Inventory",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ListingId",
                table: "Inventory");

            migrationBuilder.DropColumn(
                name: "ListingId",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "SellerId",
                table: "Warehouse");

            migrationBuilder.DropTable(
                name: "SellerProduct");

            migrationBuilder.DropTable(
                name: "Seller");

            migrationBuilder.CreateIndex(
                name: "IX_Inventory_ProductId_WarehouseId",
                table: "Inventory",
                columns: new[] { "ProductId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_Name",
                table: "Warehouse",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Inventory_Product_ProductId",
                table: "Inventory",
                column: "ProductId",
                principalTable: "Product",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Inventory_Warehouse_WarehouseId",
                table: "Inventory",
                column: "WarehouseId",
                principalTable: "Warehouse",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}