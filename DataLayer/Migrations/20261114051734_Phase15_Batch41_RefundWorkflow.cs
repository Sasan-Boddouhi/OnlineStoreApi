using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class Phase15_Batch41_RefundWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefundReason",
                table: "Payment",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "Payment",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundRequestedAtUtc",
                table: "Payment",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAtUtc",
                table: "Payment",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifyStartedAtUtc",
                table: "Payment",
                type: "datetime2",
                nullable: true);

            // Batch 41 — حفظ داده:
            // ۱) پرداخت‌های Refunded موجود همان وضعیت را نگه می‌دارند؛ لحظهٔ برگشت = آخرین تغییر وضعیت.
            migrationBuilder.Sql(
                "UPDATE [Payment] SET [RefundedAtUtc] = [LastStateChangedAtUtc] " +
                "WHERE [State] = 5 AND [RefundedAtUtc] IS NULL;");

            // ۲) «تأیید آغازشده» تا امروز فقط در AuditLog ثبت می‌شد؛ برای پرداخت‌های هنوز Pending به ستون منتقل می‌شود
            //    (اولین رکورد Payment.VerificationStarted)، وگرنه جاب‌ها و لغو سفارش آن‌ها را «بی‌خطر» می‌پنداشتند.
            migrationBuilder.Sql(
                "UPDATE p SET p.[VerifyStartedAtUtc] = a.[StartedAt] " +
                "FROM [Payment] p " +
                "INNER JOIN (SELECT [EntityId], MIN([OccurredOnUtc]) AS [StartedAt] FROM [AuditLog] " +
                "            WHERE [Action] = N'Payment.VerificationStarted' AND [EntityName] = N'Payment' " +
                "            GROUP BY [EntityId]) a " +
                "  ON a.[EntityId] = CAST(p.[PaymentId] AS nvarchar(20)) " +
                "WHERE p.[State] = 1 AND p.[VerifyStartedAtUtc] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // RefundPending (6) در کد قدیم وجود ندارد؛ پولش هنوز برنگشته، پس به Succeeded برمی‌گردد (نه Refunded).
            migrationBuilder.Sql("UPDATE [Payment] SET [State] = 2 WHERE [State] = 6;");

            migrationBuilder.DropColumn(
                name: "RefundReason",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "RefundRequestedAtUtc",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "RefundedAtUtc",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "VerifyStartedAtUtc",
                table: "Payment");
        }
    }
}
