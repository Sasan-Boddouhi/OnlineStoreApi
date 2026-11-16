using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <summary>
    /// Batch 44 (FIN-02، F-09، FIN-13 + افزوده‌های مالک از Batch 49b).
    /// <list type="bullet">
    /// <item>SellerPayout: <c>AvailableAtUtc</c> (تحویل + ۷ روز؛ بدون تاریخ تحویل = PeriodEnd)، <c>DebtOffsetAmount</c> = ۰،
    /// <c>NetAmount</c> = Amount. کسر بدهی تسویه‌های Paid قدیمی ثبت نشده بود و بازسازی نمی‌شود.</item>
    /// <item>SellerDebt: ستون <c>Amount</c> به <c>OriginalAmount</c> تغییر نام می‌دهد؛ <c>SettledAmount</c> (تسویه‌شده = مبلغ،
    /// وگرنه ۰) و <c>SettledAtUtc</c> (تسویه‌شده = ModifiedOn). بدهی‌ای که کد قدیمی بخشی از آن را کم کرده بود قابل
    /// بازسازی نیست: مبلغ فعلی‌اش مبلغ اصلی تلقی می‌شود (N20: دادهٔ موجود فقط آزمایشی است) و شناسه‌اش با کنش
    /// <c>SellerDebt.MigrationReview</c> در AuditLog (و PRINT) برای بررسی دستی ثبت می‌شود.</item>
    /// <item>User: <c>IsSystem</c>؛ حساب سیستمی Batch 49b (غیرفعال، مالک نشانی انبار پلتفرم، بدون نقش و بدون مشتری/کارمند/فروشنده)
    /// پرچم می‌خورد.</item>
    /// <item>Warehouse: ستون محاسبه‌شدهٔ <c>IsPlatform</c> و ایندکس یکتای فیلترشدهٔ <c>UX_Warehouse_SinglePlatform</c>
    /// (حداکثر یک انبار پلتفرم). اگر از قبل بیش از یک انبار پلتفرم باشد مهاجرت با پیام روشن متوقف می‌شود و چیزی تغییر نمی‌کند.</item>
    /// </list>
    /// Down همهٔ این‌ها را برمی‌گرداند؛ Amount بدهیِ تسویه‌نشده دوباره «باقی‌مانده» می‌شود (معنای قدیمی).
    /// </summary>
    public partial class Phase15_Batch44_PayoutHoldAndNet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------------- SellerDebt ----------------
            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "SellerDebt",
                newName: "OriginalAmount");

            migrationBuilder.AddColumn<decimal>(
                name: "SettledAmount",
                table: "SellerDebt",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettledAtUtc",
                table: "SellerDebt",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE [SellerDebt]
SET [SettledAmount] = [OriginalAmount],
    [SettledAtUtc] = [ModifiedOn]
WHERE [IsSettled] = 1;");

            // کد قدیمی بخشی از بدهی را با کم کردن خودِ Amount تسویه می‌کرد و تنها اثرش ModifiedOn تازه‌تر است. مبلغ اصلی
            // چنین بدهی‌ای قابل بازسازی نیست؛ شناسه‌هایش در AuditLog (و پیام PRINT) برای بررسی دستی مالی ثبت می‌شود.
            migrationBuilder.Sql(@"
DECLARE @partly nvarchar(max) = (SELECT STRING_AGG(CAST([SellerDebtId] AS nvarchar(20)), ',') FROM [SellerDebt]
                                 WHERE [IsSettled] = 0 AND [ModifiedOn] > DATEADD(second, 1, [CreatedOn]));
IF @partly IS NOT NULL
BEGIN
    INSERT INTO [AuditLog] ([OccurredOnUtc], [UserId], [Action], [EntityName], [EntityId], [Details])
    SELECT SYSUTCDATETIME(), NULL, N'SellerDebt.MigrationReview', N'SellerDebt', CAST(d.[SellerDebtId] AS nvarchar(20)),
           CONCAT(N'Batch 44: open debt was changed after it was created, probably partly settled by the old code. ',
                  N'OriginalAmount=', d.[OriginalAmount], N' is the remaining amount, not the original; review manually.')
    FROM [SellerDebt] d
    WHERE d.[IsSettled] = 0 AND d.[ModifiedOn] > DATEADD(second, 1, d.[CreatedOn])
      AND NOT EXISTS (SELECT 1 FROM [AuditLog] a WHERE a.[Action] = N'SellerDebt.MigrationReview'
                                                  AND a.[EntityId] = CAST(d.[SellerDebtId] AS nvarchar(20)));
    PRINT CONCAT(N'Batch 44: review these partly settled seller debts manually: ', @partly);
END");

            // ---------------- SellerPayout ----------------
            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableAtUtc",
                table: "SellerPayout",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<decimal>(
                name: "DebtOffsetAmount",
                table: "SellerPayout",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "SellerPayout",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(@"
UPDATE p
SET p.[AvailableAtUtc] = COALESCE(DATEADD(day, 7, g.[DeliveredAt]), p.[PeriodEnd]),
    p.[DebtOffsetAmount] = 0,
    p.[NetAmount] = p.[Amount]
FROM [SellerPayout] p
LEFT JOIN [OrderSellerGroup] g ON g.[OrderSellerGroupId] = p.[OrderSellerGroupId];");

            // ---------------- User ----------------
            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
UPDATE u
SET u.[IsSystem] = 1
FROM [User] u
WHERE u.[IsActive] = 0
  AND EXISTS (SELECT 1 FROM [Address] a
              JOIN [Warehouse] w ON w.[AddressId] = a.[AddressId]
              WHERE a.[UserId] = u.[UserId] AND w.[SellerId] IS NULL)
  AND NOT EXISTS (SELECT 1 FROM [UserRole] ur WHERE ur.[UserId] = u.[UserId])
  AND NOT EXISTS (SELECT 1 FROM [Customer] c WHERE c.[UserId] = u.[UserId])
  AND NOT EXISTS (SELECT 1 FROM [Employee] e WHERE e.[UserId] = u.[UserId])
  AND NOT EXISTS (SELECT 1 FROM [Seller] s WHERE s.[UserId] = u.[UserId]);");

            // ---------------- Warehouse ----------------
            migrationBuilder.Sql(@"
IF (SELECT COUNT(*) FROM [Warehouse] WHERE [SellerId] IS NULL) > 1
BEGIN
    DECLARE @ids nvarchar(max) = (SELECT STRING_AGG(CAST([WarehouseId] AS nvarchar(20)), ',') FROM [Warehouse] WHERE [SellerId] IS NULL);
    DECLARE @msg nvarchar(2048) = CONCAT(N'Batch 44: more than one platform warehouse (SellerId IS NULL) exists: ', @ids,
        N'. Merge their stock into one and assign or remove the others, then run the migration again.');
    THROW 50044, @msg, 1;
END");

            migrationBuilder.AddColumn<bool>(
                name: "IsPlatform",
                table: "Warehouse",
                type: "bit",
                nullable: false,
                computedColumnSql: "CASE WHEN [SellerId] IS NULL THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "UX_Warehouse_SinglePlatform",
                table: "Warehouse",
                column: "IsPlatform",
                unique: true,
                filter: "[SellerId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Warehouse_SinglePlatform",
                table: "Warehouse");

            migrationBuilder.DropColumn(
                name: "IsPlatform",
                table: "Warehouse");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "User");

            migrationBuilder.DropColumn(
                name: "AvailableAtUtc",
                table: "SellerPayout");

            migrationBuilder.DropColumn(
                name: "DebtOffsetAmount",
                table: "SellerPayout");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "SellerPayout");

            // کد قدیمی برای بدهی تسویه‌نشده «باقی‌مانده» را در Amount نگه می‌داشت.
            migrationBuilder.Sql(@"
UPDATE [SellerDebt]
SET [OriginalAmount] = [OriginalAmount] - [SettledAmount]
WHERE [IsSettled] = 0;");

            migrationBuilder.DropColumn(
                name: "SettledAmount",
                table: "SellerDebt");

            migrationBuilder.DropColumn(
                name: "SettledAtUtc",
                table: "SellerDebt");

            migrationBuilder.RenameColumn(
                name: "OriginalAmount",
                table: "SellerDebt",
                newName: "Amount");
        }
    }
}
