using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_FinancialsAndSplitting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionRate",
                table: "Seller",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            // گارد صریح: بدون آن، EF مقدار پیش‌فرض 0 می‌گذاشت و کلید خارجی می‌شکست.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [OrderItem] WHERE [ListingId] IS NULL)
                    THROW 50010, N'Phase3: آیتم سفارش بدون Listing باقی مانده است.', 1;
            ");

            migrationBuilder.AlterColumn<int>(
                name: "ListingId",
                table: "OrderItem",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderSellerGroupId",
                table: "OrderItem",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderSellerGroup",
                columns: table => new
                {
                    OrderSellerGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CommissionRate = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SellerPayable = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSellerGroup", x => x.OrderSellerGroupId);
                    table.ForeignKey(
                        name: "FK_OrderSellerGroup_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderSellerGroup_Seller_SellerId",
                        column: x => x.SellerId,
                        principalTable: "Seller",
                        principalColumn: "SellerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SellerPayout",
                columns: table => new
                {
                    SellerPayoutId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerId = table.Column<int>(type: "int", nullable: false),
                    OrderSellerGroupId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerPayout", x => x.SellerPayoutId);
                    table.ForeignKey(
                        name: "FK_SellerPayout_OrderSellerGroup_OrderSellerGroupId",
                        column: x => x.OrderSellerGroupId,
                        principalTable: "OrderSellerGroup",
                        principalColumn: "OrderSellerGroupId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SellerPayout_Seller_SellerId",
                        column: x => x.SellerId,
                        principalTable: "Seller",
                        principalColumn: "SellerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_OrderSellerGroupId",
                table: "OrderItem",
                column: "OrderSellerGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSellerGroup_OrderId_SellerId",
                table: "OrderSellerGroup",
                columns: new[] { "OrderId", "SellerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderSellerGroup_SellerId",
                table: "OrderSellerGroup",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayout_OrderSellerGroupId",
                table: "SellerPayout",
                column: "OrderSellerGroupId",
                unique: true,
                filter: "[OrderSellerGroupId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayout_SellerId",
                table: "SellerPayout",
                column: "SellerId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItem_OrderSellerGroup_OrderSellerGroupId",
                table: "OrderItem",
                column: "OrderSellerGroupId",
                principalTable: "OrderSellerGroup",
                principalColumn: "OrderSellerGroupId",
                onDelete: ReferentialAction.Restrict);

            // ---------- بک‌فیل گروه‌های فروشنده از آیتم‌های تاریخی ----------
            // نرخ کمیسیون صفر گذاشته می‌شود: اعمال نرخ امروز روی سفارش گذشته،
            // تغییر مالی گذشته‌نگر و بدون تأیید است.
            migrationBuilder.Sql(@"
                INSERT INTO [OrderSellerGroup]
                    ([OrderId], [SellerId], [SubTotal], [CommissionRate], [CommissionAmount], [SellerPayable], [Status], [CreatedOn], [ModifiedOn])
                SELECT
                    oi.[OrderId],
                    sp.[SellerId],
                    SUM(oi.[Quantity] * oi.[UnitPrice]),
                    0, 0, 0, 0,
                    SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM [OrderItem] oi
                JOIN [SellerProduct] sp ON sp.[ListingId] = oi.[ListingId]
                WHERE NOT EXISTS (
                    SELECT 1 FROM [OrderSellerGroup] g
                    WHERE g.[OrderId] = oi.[OrderId] AND g.[SellerId] = sp.[SellerId])
                GROUP BY oi.[OrderId], sp.[SellerId];

                UPDATE oi
                SET oi.[OrderSellerGroupId] = g.[OrderSellerGroupId]
                FROM [OrderItem] oi
                JOIN [SellerProduct] sp ON sp.[ListingId] = oi.[ListingId]
                JOIN [OrderSellerGroup] g
                    ON g.[OrderId] = oi.[OrderId] AND g.[SellerId] = sp.[SellerId]
                WHERE oi.[OrderSellerGroupId] IS NULL;
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM [OrderItem] WHERE [OrderSellerGroupId] IS NULL)
                    THROW 50011, N'Phase3: آیتم سفارش به هیچ گروه فروشنده‌ای متصل نشد.', 1;
            ");

            migrationBuilder.AlterColumn<int>(
                name: "OrderSellerGroupId",
                table: "OrderItem",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItem_OrderSellerGroup_OrderSellerGroupId",
                table: "OrderItem");

            migrationBuilder.DropTable(
                name: "SellerPayout");

            migrationBuilder.DropTable(
                name: "OrderSellerGroup");

            migrationBuilder.DropIndex(
                name: "IX_OrderItem_OrderSellerGroupId",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "CommissionRate",
                table: "Seller");

            migrationBuilder.DropColumn(
                name: "OrderSellerGroupId",
                table: "OrderItem");

            migrationBuilder.AlterColumn<int>(
                name: "ListingId",
                table: "OrderItem",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
