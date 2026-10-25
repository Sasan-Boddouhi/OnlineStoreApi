using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <summary>
    /// Phase 6 — Variants &amp; Attributes.
    ///
    /// ⚠️ این مایگریشن دستی بازنویسی شده است.
    ///
    /// خروجی خام EF دستور زیر را تولید کرده بود:
    ///     migrationBuilder.RenameColumn("ProductId" → "VariantId", table: "SellerProduct");
    ///
    /// این یک فاجعهٔ خاموش بود: ProductId و VariantId دو فضای شناسهٔ کاملاً متفاوت‌اند.
    /// Listing با ProductId = 5 بعد از rename به VariantId = 5 اشاره می‌کرد —
    /// یعنی یا به واریانتی از یک محصول دیگر وصل می‌شد، یا FK می‌شکست.
    /// همان الگوی Phase 2 که داده را نابود کرد.
    ///
    /// مسیر امن: ستون جدید nullable اضافه می‌شود، برای هر محصول یک واریانت پیش‌فرض
    /// ساخته می‌شود، مقداردهی انجام می‌شود، با THROW محافظت می‌شود، و فقط بعد از
    /// اطمینان از بک‌فیل کامل، ستون قدیمی حذف می‌گردد.
    ///
    /// Inventory.ListingId عمداً دست‌نخورده است: موجودی همیشه به Listing تعلق دارد.
    /// </summary>
    public partial class Phase6_VariantsAndAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ============ ۱) جداول جدید ============

            migrationBuilder.CreateTable(
                name: "ProductVariant",
                columns: table => new
                {
                    VariantId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariant", x => x.VariantId);
                    table.ForeignKey(
                        name: "FK_ProductVariant_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttribute",
                columns: table => new
                {
                    AttributeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttribute", x => x.AttributeId);
                    table.ForeignKey(
                        name: "FK_ProductAttribute_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeValue",
                columns: table => new
                {
                    ValueId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    AttributeId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeValue", x => x.ValueId);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValue_ProductAttribute_AttributeId",
                        column: x => x.AttributeId,
                        principalTable: "ProductAttribute",
                        principalColumn: "AttributeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValue_ProductVariant_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "VariantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttribute_ProductId_Name",
                table: "ProductAttribute",
                columns: new[] { "ProductId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValue_AttributeId",
                table: "ProductAttributeValue",
                column: "AttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValue_VariantId_AttributeId",
                table: "ProductAttributeValue",
                columns: new[] { "VariantId", "AttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_ProductId",
                table: "ProductVariant",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Sku",
                table: "ProductVariant",
                column: "Sku",
                unique: true,
                filter: "[Sku] IS NOT NULL");

            // ============ ۲) ستون جدید در SellerProduct (nullable، بدون FK) ============

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "SellerProduct",
                type: "int",
                nullable: true);

            // ============ ۳) بک‌فیل: یک واریانت پیش‌فرض برای هر محصول ============

            migrationBuilder.Sql(@"
                INSERT INTO [ProductVariant] ([ProductId], [Name], [Sku], [Barcode], [IsActive], [CreatedOn], [ModifiedOn])
                SELECT p.[ProductId], N'Default', NULL, NULL, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM [Product] p;
            ");

            // ============ ۴) اتصال Listingهای موجود به واریانت پیش‌فرضِ محصول خودشان ============

            migrationBuilder.Sql(@"
                UPDATE sp
                SET sp.[VariantId] = pv.[VariantId]
                FROM [SellerProduct] sp
                INNER JOIN [ProductVariant] pv
                    ON pv.[ProductId] = sp.[ProductId]
                   AND pv.[Name] = N'Default';
            ");

            // ============ ۵) محافظ: اگر حتی یک ردیف بک‌فیل نشد، کل مایگریشن متوقف شود ============

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [SellerProduct] WHERE [VariantId] IS NULL)
                BEGIN
                    THROW 50020, N'Phase 6 backfill failed: at least one SellerProduct has no ProductVariant. Aborting to prevent data loss.', 1;
                END
            ");

            // ============ ۶) حالا می‌توان NOT NULL کرد ============

            migrationBuilder.AlterColumn<int>(
                name: "VariantId",
                table: "SellerProduct",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // ============ ۷) حذف ستون قدیمی — فقط بعد از بک‌فیل موفق ============

            migrationBuilder.DropForeignKey(
                name: "FK_SellerProduct_Product_ProductId",
                table: "SellerProduct");

            migrationBuilder.DropIndex(
                name: "IX_SellerProduct_SellerId_ProductId",
                table: "SellerProduct");

            migrationBuilder.DropIndex(
                name: "IX_SellerProduct_ProductId",
                table: "SellerProduct");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "SellerProduct");

            // ============ ۸) ایندکس‌ها و FK جدید ============

            migrationBuilder.CreateIndex(
                name: "IX_SellerProduct_SellerId_VariantId",
                table: "SellerProduct",
                columns: new[] { "SellerId", "VariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerProduct_VariantId",
                table: "SellerProduct",
                column: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_SellerProduct_ProductVariant_VariantId",
                table: "SellerProduct",
                column: "VariantId",
                principalTable: "ProductVariant",
                principalColumn: "VariantId",
                onDelete: ReferentialAction.Restrict);

            // ============ ۹) OrderItem / CartItem — snapshot واریانت ============

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "OrderItem",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "CartItem",
                type: "int",
                nullable: true);

            // بک‌فیل از روی Listing؛ اینجا اجباری نیست چون ستون nullable است،
            // اما بدون آن سابقهٔ سفارش‌های قدیمی بی‌دلیل خالی می‌ماند.
            migrationBuilder.Sql(@"
                UPDATE oi
                SET oi.[VariantId] = sp.[VariantId]
                FROM [OrderItem] oi
                INNER JOIN [SellerProduct] sp ON sp.[ListingId] = oi.[ListingId];
            ");

            migrationBuilder.Sql(@"
                UPDATE ci
                SET ci.[VariantId] = sp.[VariantId]
                FROM [CartItem] ci
                INNER JOIN [SellerProduct] sp ON sp.[ListingId] = ci.[ListingId];
            ");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_VariantId",
                table: "OrderItem",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItem_VariantId",
                table: "CartItem",
                column: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItem_ProductVariant_VariantId",
                table: "OrderItem",
                column: "VariantId",
                principalTable: "ProductVariant",
                principalColumn: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_CartItem_ProductVariant_VariantId",
                table: "CartItem",
                column: "VariantId",
                principalTable: "ProductVariant",
                principalColumn: "VariantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ============ بازگشت OrderItem / CartItem ============

            migrationBuilder.DropForeignKey(
                name: "FK_CartItem_ProductVariant_VariantId",
                table: "CartItem");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItem_ProductVariant_VariantId",
                table: "OrderItem");

            migrationBuilder.DropIndex(
                name: "IX_OrderItem_VariantId",
                table: "OrderItem");

            migrationBuilder.DropIndex(
                name: "IX_CartItem_VariantId",
                table: "CartItem");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "CartItem");

            // ============ بازگشت SellerProduct به ProductId ============
            // همان منطق، برعکس: اول ستون nullable، بعد بک‌فیل از واریانت، بعد محافظ، بعد NOT NULL.

            migrationBuilder.DropForeignKey(
                name: "FK_SellerProduct_ProductVariant_VariantId",
                table: "SellerProduct");

            migrationBuilder.DropIndex(
                name: "IX_SellerProduct_SellerId_VariantId",
                table: "SellerProduct");

            migrationBuilder.DropIndex(
                name: "IX_SellerProduct_VariantId",
                table: "SellerProduct");

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "SellerProduct",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE sp
                SET sp.[ProductId] = pv.[ProductId]
                FROM [SellerProduct] sp
                INNER JOIN [ProductVariant] pv ON pv.[VariantId] = sp.[VariantId];
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [SellerProduct] WHERE [ProductId] IS NULL)
                BEGIN
                    THROW 50021, N'Phase 6 rollback failed: at least one SellerProduct has no Product. Aborting to prevent data loss.', 1;
                END
            ");

            migrationBuilder.AlterColumn<int>(
                name: "ProductId",
                table: "SellerProduct",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "SellerProduct");

            migrationBuilder.CreateIndex(
                name: "IX_SellerProduct_SellerId_ProductId",
                table: "SellerProduct",
                columns: new[] { "SellerId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerProduct_ProductId",
                table: "SellerProduct",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_SellerProduct_Product_ProductId",
                table: "SellerProduct",
                column: "ProductId",
                principalTable: "Product",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);

            // ============ حذف جداول جدید ============

            migrationBuilder.DropTable(
                name: "ProductAttributeValue");

            migrationBuilder.DropTable(
                name: "ProductAttribute");

            migrationBuilder.DropTable(
                name: "ProductVariant");
        }
    }
}