using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_DropPaymentStatus : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// Phase 9 (R39): حذف ستون تکراری Status.
        ///
        /// Rule 7 — چرا حذف داده اینجا امن است:
        ///   • تا پیش از فاز ۸، Status منبع حقیقت بود.
        ///   • مایگریشن فاز ۸ دقیقاً همان Status را در State بازنویسی کرد
        ///     (Completed→Succeeded، Failed→Failed، Refunded→Refunded، Pending→Pending).
        ///   • از آن به بعد هر دو فقط از داخل TransitionTo نوشته می‌شدند.
        ///   • State مجموعهٔ بزرگ‌تری از حالات را پوشش می‌دهد (Initiated و Cancelled اضافه).
        /// پس هیچ اطلاعاتی در Status نیست که در State نباشد.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Payment");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Payment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Rule 7 — برگشت: پیش‌فرض 0 (= Pending) برای همه، دروغ بزرگ می‌گوید.
            // همان نگاشت فاز ۸ را برعکس اعمال می‌کنیم تا Status واقعی برگردد.
            migrationBuilder.Sql(@"
UPDATE [Payment]
SET [Status] = CASE [State]
                    WHEN 2 THEN 1
                    WHEN 3 THEN 2
                    WHEN 4 THEN 2
                    WHEN 5 THEN 3
                    ELSE 0
                END;
");
        }
    }
}
