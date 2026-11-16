using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <summary>
    /// Batch 46 (F-12، N9): <c>User.PhoneNumberConfirmed bit NOT NULL DEFAULT 0</c> و جدول <c>OtpCode</c> (فقط هش کد و توکن).
    /// <para>
    /// حفظ داده (N9): همهٔ کاربران موجود تأییدشده علامت می‌خورند (grandfathered)، به‌جز حساب سیستمی بی‌اثر (IsSystem) که هرگز
    /// شماره‌اش تأیید نمی‌شود. کاربری که پس از این مهاجرت ساخته شود با پیش‌فرض ۰ شروع می‌کند.
    /// Down ستون و جدول را برمی‌دارد؛ هیچ کاربری حذف نمی‌شود (کدهای OTP باز از دست می‌روند که عمرشان دقیقه‌هاست).
    /// </para>
    /// </summary>
    public partial class Phase15_Batch46_PhoneVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // N9: کاربران موجود تأییدشده‌اند (حساب سیستمی نه).
            migrationBuilder.Sql("UPDATE [User] SET [PhoneNumberConfirmed] = 1 WHERE [IsSystem] = 0;");

            migrationBuilder.CreateTable(
                name: "OtpCode",
                columns: table => new
                {
                    OtpCodeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PhoneNumber = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerificationTokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    VerificationExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerificationUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpCode", x => x.OtpCodeId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OtpCode_CreatedAtUtc",
                table: "OtpCode",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OtpCode_ExpiresAtUtc",
                table: "OtpCode",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OtpCode_PhoneNumber_CreatedAtUtc",
                table: "OtpCode",
                columns: new[] { "PhoneNumber", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpCode_PhoneNumber_Purpose",
                table: "OtpCode",
                columns: new[] { "PhoneNumber", "Purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OtpCode");

            migrationBuilder.DropColumn(
                name: "PhoneNumberConfirmed",
                table: "User");
        }
    }
}
