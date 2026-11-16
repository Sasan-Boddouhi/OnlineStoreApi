using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <summary>
    /// Batch 45 (FIN-04، FIN-06، FIN-09، FIN-10، FIN-14؛ N4 تا N7؛ اصلاحیه‌های ۱ و ۲ مالک پس از Batch 44).
    /// <list type="bullet">
    /// <item>Coupon: <c>FundedBy</c> (۰ = پلتفرم) — همهٔ کوپن‌های موجود پلتفرمی‌اند.</item>
    /// <item>OrderSellerGroup: <c>ShippingAmount</c> (هزینهٔ ارسال بسته، بستانکار فروشنده) و <c>PlatformDiscountShare</c>
    /// (سهم کوپن که پلتفرم می‌دهد). تاریخچه بازمحاسبه نمی‌شود: گروه‌های پرداخت‌شده ۰ می‌گیرند و <c>SellerPayable</c>
    /// ذخیره‌شده‌شان دست نمی‌خورد. فقط گروه‌های هنوز پرداخت‌نشده (Status = Pending) <c>PlatformDiscountShare = DiscountAmount</c> و
    /// <c>ShippingAmount = ShippingCostShare</c> می‌گیرند تا هنگام پرداخت با قاعدهٔ تازه تسویه شوند. برای تسویه‌های Pending
    /// اختلاف قاعدهٔ قدیم و جدید فقط با کنش <c>OrderSellerGroup.PricingReview</c> در AuditLog (و PRINT) گزارش می‌شود.</item>
    /// <item>SellerDebt: ستون صریح <c>RemainingAmount = OriginalAmount − SettledAmount</c>. بدهی‌هایی که مهاجرت Batch 44
    /// با حدس «تغییر بعد از ساخت» علامت زده بود (<c>SellerDebt.MigrationReview</c>) با کنش
    /// <c>SellerDebt.RemainingAmountReview</c> (و PRINT) برای بررسی دستی ثبت می‌شوند؛ مقدارشان بهترین بازسازی ممکن است.</item>
    /// </list>
    /// Down ستون‌ها را برمی‌دارد؛ رکوردهای بررسی در AuditLog به‌عنوان سند می‌مانند.
    /// </summary>
    public partial class Phase15_Batch45_PricingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RemainingAmount",
                table: "SellerDebt",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformDiscountShare",
                table: "OrderSellerGroup",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingAmount",
                table: "OrderSellerGroup",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "FundedBy",
                table: "Coupon",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // ---------------- SellerDebt.RemainingAmount ----------------
            migrationBuilder.Sql(@"
UPDATE [SellerDebt] SET [RemainingAmount] = [OriginalAmount] - [SettledAmount];");

            migrationBuilder.Sql(@"
DECLARE @flagged nvarchar(max) = (
    SELECT STRING_AGG(CAST(d.[SellerDebtId] AS nvarchar(20)), ',') FROM [SellerDebt] d
    WHERE EXISTS (SELECT 1 FROM [AuditLog] a WHERE a.[Action] = N'SellerDebt.MigrationReview' AND a.[EntityName] = N'SellerDebt'
                                              AND a.[EntityId] = CAST(d.[SellerDebtId] AS nvarchar(20))));
IF @flagged IS NOT NULL
BEGIN
    INSERT INTO [AuditLog] ([OccurredOnUtc], [UserId], [Action], [EntityName], [EntityId], [Details])
    SELECT SYSUTCDATETIME(), NULL, N'SellerDebt.RemainingAmountReview', N'SellerDebt', CAST(d.[SellerDebtId] AS nvarchar(20)),
           CONCAT(N'Batch 45: RemainingAmount back-filled as OriginalAmount - SettledAmount = ', d.[RemainingAmount],
                  N' (OriginalAmount=', d.[OriginalAmount], N', SettledAmount=', d.[SettledAmount],
                  N'). Batch 44 flagged this debt as probably partly settled by the old code; this is the best reconstruction, review manually.')
    FROM [SellerDebt] d
    WHERE EXISTS (SELECT 1 FROM [AuditLog] a WHERE a.[Action] = N'SellerDebt.MigrationReview' AND a.[EntityName] = N'SellerDebt'
                                              AND a.[EntityId] = CAST(d.[SellerDebtId] AS nvarchar(20)))
      AND NOT EXISTS (SELECT 1 FROM [AuditLog] r WHERE r.[Action] = N'SellerDebt.RemainingAmountReview'
                                                  AND r.[EntityId] = CAST(d.[SellerDebtId] AS nvarchar(20)));
    PRINT CONCAT(N'Batch 45: RemainingAmount of seller debts flagged by Batch 44 is a reconstruction; review: ', @flagged);
END");

            // ---------------- OrderSellerGroup: گروه‌های پرداخت‌نشده با قاعدهٔ تازه تسویه می‌شوند ----------------
            // همهٔ کوپن‌های موجود پلتفرمی‌اند و ShippingCostShare همان سهمی از هزینهٔ ارسال است که مشتری برای این بسته پرداخته.
            migrationBuilder.Sql(@"
UPDATE [OrderSellerGroup]
SET [PlatformDiscountShare] = [DiscountAmount],
    [ShippingAmount] = [ShippingCostShare]
WHERE [Status] = 0;");

            // ---------------- تسویه‌های Pending: فقط گزارش اختلاف، بدون تغییر ----------------
            migrationBuilder.Sql(@"
DECLARE @diff nvarchar(max) = (
    SELECT STRING_AGG(CAST(g.[OrderSellerGroupId] AS nvarchar(20)), ',')
    FROM [OrderSellerGroup] g JOIN [SellerPayout] p ON p.[OrderSellerGroupId] = g.[OrderSellerGroupId]
    WHERE p.[Status] = 0
      AND g.[SubTotal] - ROUND(g.[SubTotal] * g.[CommissionRate] / 100, 0) + g.[ShippingCostShare] <> g.[SellerPayable]);
IF @diff IS NOT NULL
BEGIN
    INSERT INTO [AuditLog] ([OccurredOnUtc], [UserId], [Action], [EntityName], [EntityId], [Details])
    SELECT SYSUTCDATETIME(), NULL, N'OrderSellerGroup.PricingReview', N'OrderSellerGroup', CAST(g.[OrderSellerGroupId] AS nvarchar(20)),
           CONCAT(N'Batch 45: pending payout ', p.[SellerPayoutId], N' keeps the stored SellerPayable=', g.[SellerPayable],
                  N'; under the new rule (platform coupon not deducted, commission before the coupon, shipping credited) it would be ',
                  g.[SubTotal] - ROUND(g.[SubTotal] * g.[CommissionRate] / 100, 0) + g.[ShippingCostShare],
                  N' (difference ', g.[SubTotal] - ROUND(g.[SubTotal] * g.[CommissionRate] / 100, 0) + g.[ShippingCostShare] - g.[SellerPayable],
                  N'). Not changed automatically; finance to review.')
    FROM [OrderSellerGroup] g JOIN [SellerPayout] p ON p.[OrderSellerGroupId] = g.[OrderSellerGroupId]
    WHERE p.[Status] = 0
      AND g.[SubTotal] - ROUND(g.[SubTotal] * g.[CommissionRate] / 100, 0) + g.[ShippingCostShare] <> g.[SellerPayable]
      AND NOT EXISTS (SELECT 1 FROM [AuditLog] r WHERE r.[Action] = N'OrderSellerGroup.PricingReview'
                                                  AND r.[EntityId] = CAST(g.[OrderSellerGroupId] AS nvarchar(20)));
    PRINT CONCAT(N'Batch 45: pending payouts whose seller payable differs under the new pricing rule (groups): ', @diff);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemainingAmount",
                table: "SellerDebt");

            migrationBuilder.DropColumn(
                name: "PlatformDiscountShare",
                table: "OrderSellerGroup");

            migrationBuilder.DropColumn(
                name: "ShippingAmount",
                table: "OrderSellerGroup");

            migrationBuilder.DropColumn(
                name: "FundedBy",
                table: "Coupon");
        }
    }
}
