using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class AddReplacedByTokenId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplacedByTokenHash",
                table: "RefreshToken");

            migrationBuilder.AddColumn<int>(
                name: "ReplacedByTokenId",
                table: "RefreshToken",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_ReplacedByTokenId",
                table: "RefreshToken",
                column: "ReplacedByTokenId");

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshToken_RefreshToken_ReplacedByTokenId",
                table: "RefreshToken",
                column: "ReplacedByTokenId",
                principalTable: "RefreshToken",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefreshToken_RefreshToken_ReplacedByTokenId",
                table: "RefreshToken");

            migrationBuilder.DropIndex(
                name: "IX_RefreshToken_ReplacedByTokenId",
                table: "RefreshToken");

            migrationBuilder.DropColumn(
                name: "ReplacedByTokenId",
                table: "RefreshToken");

            migrationBuilder.AddColumn<string>(
                name: "ReplacedByTokenHash",
                table: "RefreshToken",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
