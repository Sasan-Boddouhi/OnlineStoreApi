using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class Phase15_Batch43_PaymentGatewayDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardPanMasked",
                table: "Payment",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GatewayFee",
                table: "Payment",
                type: "decimal(18,0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatewayFeeType",
                table: "Payment",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardPanMasked",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "GatewayFee",
                table: "Payment");

            migrationBuilder.DropColumn(
                name: "GatewayFeeType",
                table: "Payment");
        }
    }
}
