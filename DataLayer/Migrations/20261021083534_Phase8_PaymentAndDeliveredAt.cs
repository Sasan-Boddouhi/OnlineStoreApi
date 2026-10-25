using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class Phase8_PaymentAndDeliveredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Gateway",
                table: "Payment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GatewayReference",
                table: "Payment",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Payment",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastStateChangedAtUtc",
                table: "Payment",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "OrderId",
                table: "Payment",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "Payment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "OrderSellerGroup",
                type: "datetime2",
                nullable: true);

            // ===============================================================
            // Phase 8 — بازگردانی پرداخت‌های تاریخی.
            //
            // Rule 7: ستون State با پیش‌فرض 0 (= Initiated) اضافه می‌شد و هر
            // پرداخت قدیمیِ Completed برچسب «شروع‌شده» می‌گرفت؛ یعنی داشبورد
            // مالی دروغ می‌گفت. Status منبع حقیقت تاریخی است، پس State از آن پر می‌شود:
            //   Completed(1) → Succeeded(2)
            //   Failed(2)    → Failed(3)
            //   Refunded(3)  → Refunded(5)
            //   Pending(0)   → Pending(1)
            // ===============================================================
            migrationBuilder.Sql(@"
UPDATE [Payment]
SET [State] = CASE [Status]
                    WHEN 1 THEN 2
                    WHEN 2 THEN 3
                    WHEN 3 THEN 5
                    ELSE 1
                END;
");

            // پیش‌فرض 0001-01-01 برای «آخرین تغییر وضعیت» بی‌معناست.
            migrationBuilder.Sql(@"
UPDATE [Payment]
SET [LastStateChangedAtUtc] = GETUTCDATE()
WHERE [LastStateChangedAtUtc] < '2000-01-01';
");

            // پرداخت‌های تاریخی به سفارششان وصل می‌شوند تا برگشت وجه و
            // گزارش‌های مالی آن‌ها را ببینند.
            migrationBuilder.Sql(@"
UPDATE p
SET p.[OrderId] = i.[OrderId]
FROM [Payment] p
INNER JOIN [Invoice] i ON i.[InvoiceId] = p.[InvoiceId]
WHERE p.[OrderId] IS NULL;
");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_IdempotencyKey",
                table: "Payment",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_OrderId",
                table: "Payment",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_State",
                table: "Payment",
                column: "State");

            migrationBuilder.AddForeignKey(
                name: "FK_Payment_Order_OrderId",
                table: "Payment",
                column: "OrderId",
                principalTable: "Order",
                principalColumn: "OrderId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payment_Order_OrderId",
                table: "Payment");

            migrationBuilder.DropIndex(
                name: "IX_Payment_IdempotencyKey",
                table: "Payment");

            migrationBuilder.DropIndex(
                name: "IX_Payment_OrderId",
                table: "Payment");

            migrationBuilder.DropIndex(
                name: "IX_Payment_State",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "Gateway",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "GatewayReference",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "LastStateChangedAtUtc",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "OrderSellerGroup");
        }
    }
}
