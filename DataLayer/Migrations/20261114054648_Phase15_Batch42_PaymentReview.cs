using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class Phase15_Batch42_PaymentReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "Payment",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewRequiredAtUtc",
                table: "Payment",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewResolvedAtUtc",
                table: "Payment",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewResolvedByUserId",
                table: "Payment",
                type: "int",
                nullable: true);

            // Batch 42 — حفظ داده: پرداخت‌هایی که تا امروز فقط در AuditLog با «Payment.ReviewRequired» ثبت شده بودند،
            // با اولین زمان ثبت و آخرین شرح (حداکثر ۱۰۰۰ کاراکتر) در صف بررسی باز ظاهر می‌شوند. بستن آن‌ها با ادمین است.
            migrationBuilder.Sql(
                "UPDATE p SET p.[ReviewRequiredAtUtc] = a.[FirstAt], " +
                "             p.[ReviewNote] = LEFT(ISNULL(last.[Details], N'review required (migrated from audit log)'), 1000) " +
                "FROM [Payment] p " +
                "INNER JOIN (SELECT [EntityId], MIN([OccurredOnUtc]) AS [FirstAt] FROM [AuditLog] " +
                "            WHERE [Action] = N'Payment.ReviewRequired' AND [EntityName] = N'Payment' " +
                "            GROUP BY [EntityId]) a " +
                "  ON a.[EntityId] = CAST(p.[PaymentId] AS nvarchar(20)) " +
                "OUTER APPLY (SELECT TOP (1) l.[Details] FROM [AuditLog] l " +
                "             WHERE l.[Action] = N'Payment.ReviewRequired' AND l.[EntityName] = N'Payment' " +
                "               AND l.[EntityId] = a.[EntityId] " +
                "             ORDER BY l.[OccurredOnUtc] DESC, l.[AuditLogId] DESC) last " +
                "WHERE p.[ReviewRequiredAtUtc] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "ReviewRequiredAtUtc",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "ReviewResolvedAtUtc",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "ReviewResolvedByUserId",
                table: "Payment");
        }
    }
}
