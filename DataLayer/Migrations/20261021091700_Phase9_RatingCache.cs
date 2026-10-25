using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_RatingCache : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// Phase 9 (R33) — کش امتیاز.
        ///
        /// Rule 7: دو ستون غیرقابل‌NULL با پیش‌فرض 0 اضافه می‌شوند. بدون backfill،
        /// همهٔ محصولات «۰ از ۰ نظر» نشان می‌دادند — یعنی صفحهٔ محصول بی‌صدا خراب
        /// می‌شد و هیچ استثنایی هم رخ نمی‌داد. پس از AddColumn، مقادیر از
        /// نظرات تأییدشده محاسبه و نوشته می‌شوند.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AverageRating",
                table: "Seller",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "ReviewCount",
                table: "Seller",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "AverageRating",
                table: "Product",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "ReviewCount",
                table: "Product",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // ===== backfill: فقط نظرات تأییدشده، گرد شده به دو رقم =====
            migrationBuilder.Sql(@"
UPDATE p
SET p.[ReviewCount]   = c.[Cnt],
    p.[AverageRating] = ROUND(c.[Avg], 2)
FROM [Product] p
INNER JOIN (
    SELECT [ProductId], COUNT(*) AS [Cnt], AVG(CAST([Rating] AS float)) AS [Avg]
    FROM [ProductReview]
    WHERE [IsApproved] = 1
    GROUP BY [ProductId]
) c ON c.[ProductId] = p.[ProductId];
");

            migrationBuilder.Sql(@"
UPDATE s
SET s.[ReviewCount]   = c.[Cnt],
    s.[AverageRating] = ROUND(c.[Avg], 2)
FROM [Seller] s
INNER JOIN (
    SELECT [SellerId], COUNT(*) AS [Cnt], AVG(CAST([Rating] AS float)) AS [Avg]
    FROM [SellerReview]
    WHERE [IsApproved] = 1
    GROUP BY [SellerId]
) c ON c.[SellerId] = s.[SellerId];
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageRating",
                table: "Seller");

            migrationBuilder.DropColumn(
                name: "ReviewCount",
                table: "Seller");

            migrationBuilder.DropColumn(
                name: "AverageRating",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "ReviewCount",
                table: "Product");
        }
    }
}
