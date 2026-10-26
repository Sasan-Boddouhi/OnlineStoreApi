using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_EntityFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Address_City_CityId",
                table: "Address");

            migrationBuilder.DropForeignKey(
                name: "FK_City_Province_ProvinceId",
                table: "City");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoice_Order_OrderId",
                table: "Invoice");

            migrationBuilder.DropForeignKey(
                name: "FK_Order_Customer_CustomerId",
                table: "Order");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItem_ProductVariant_VariantId",
                table: "OrderItem");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderSellerGroup_Order_OrderId",
                table: "OrderSellerGroup");

            migrationBuilder.DropForeignKey(
                name: "FK_Payment_Invoice_InvoiceId",
                table: "Payment");

            migrationBuilder.DropForeignKey(
                name: "FK_Product_ProductSubcategory_SubcategoryId",
                table: "Product");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductSubcategory_ProductCategory_CategoryId",
                table: "ProductSubcategory");

            migrationBuilder.DropIndex(
                name: "IX_ProductReview_OrderItemId",
                table: "ProductReview");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "Customer");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "User",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "User",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SellerProduct",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AlterColumn<string>(
                name: "ImageUrl",
                table: "Product",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Order",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayout_Status",
                table: "SellerPayout",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProductReview_OrderItemId",
                table: "ProductReview",
                column: "OrderItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Product_IsActive",
                table: "Product",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Order_Status",
                table: "Order",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Invoice_Status",
                table: "Invoice",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId_Action",
                table: "AuditLog",
                columns: new[] { "UserId", "Action" });

            migrationBuilder.AddForeignKey(
                name: "FK_Address_City_CityId",
                table: "Address",
                column: "CityId",
                principalTable: "City",
                principalColumn: "CityId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_City_Province_ProvinceId",
                table: "City",
                column: "ProvinceId",
                principalTable: "Province",
                principalColumn: "ProvinceId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoice_Order_OrderId",
                table: "Invoice",
                column: "OrderId",
                principalTable: "Order",
                principalColumn: "OrderId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Order_Customer_CustomerId",
                table: "Order",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "CustomerId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItem_ProductVariant_VariantId",
                table: "OrderItem",
                column: "VariantId",
                principalTable: "ProductVariant",
                principalColumn: "VariantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderSellerGroup_Order_OrderId",
                table: "OrderSellerGroup",
                column: "OrderId",
                principalTable: "Order",
                principalColumn: "OrderId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payment_Invoice_InvoiceId",
                table: "Payment",
                column: "InvoiceId",
                principalTable: "Invoice",
                principalColumn: "InvoiceId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Product_ProductSubcategory_SubcategoryId",
                table: "Product",
                column: "SubcategoryId",
                principalTable: "ProductSubcategory",
                principalColumn: "SubcategoryId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSubcategory_ProductCategory_CategoryId",
                table: "ProductSubcategory",
                column: "CategoryId",
                principalTable: "ProductCategory",
                principalColumn: "CategoryId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Address_City_CityId",
                table: "Address");

            migrationBuilder.DropForeignKey(
                name: "FK_City_Province_ProvinceId",
                table: "City");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoice_Order_OrderId",
                table: "Invoice");

            migrationBuilder.DropForeignKey(
                name: "FK_Order_Customer_CustomerId",
                table: "Order");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItem_ProductVariant_VariantId",
                table: "OrderItem");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderSellerGroup_Order_OrderId",
                table: "OrderSellerGroup");

            migrationBuilder.DropForeignKey(
                name: "FK_Payment_Invoice_InvoiceId",
                table: "Payment");

            migrationBuilder.DropForeignKey(
                name: "FK_Product_ProductSubcategory_SubcategoryId",
                table: "Product");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductSubcategory_ProductCategory_CategoryId",
                table: "ProductSubcategory");

            migrationBuilder.DropIndex(
                name: "IX_SellerPayout_Status",
                table: "SellerPayout");

            migrationBuilder.DropIndex(
                name: "IX_ProductReview_OrderItemId",
                table: "ProductReview");

            migrationBuilder.DropIndex(
                name: "IX_Product_IsActive",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "IX_Order_Status",
                table: "Order");

            migrationBuilder.DropIndex(
                name: "IX_Invoice_Status",
                table: "Invoice");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_UserId_Action",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SellerProduct");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Order");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "User",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(36)",
                oldMaxLength: 36);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "User",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ImageUrl",
                table: "Product",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "OrderItem",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Customer",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Customer",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ProductReview_OrderItemId",
                table: "ProductReview",
                column: "OrderItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Address_City_CityId",
                table: "Address",
                column: "CityId",
                principalTable: "City",
                principalColumn: "CityId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_City_Province_ProvinceId",
                table: "City",
                column: "ProvinceId",
                principalTable: "Province",
                principalColumn: "ProvinceId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoice_Order_OrderId",
                table: "Invoice",
                column: "OrderId",
                principalTable: "Order",
                principalColumn: "OrderId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Order_Customer_CustomerId",
                table: "Order",
                column: "CustomerId",
                principalTable: "Customer",
                principalColumn: "CustomerId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItem_ProductVariant_VariantId",
                table: "OrderItem",
                column: "VariantId",
                principalTable: "ProductVariant",
                principalColumn: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderSellerGroup_Order_OrderId",
                table: "OrderSellerGroup",
                column: "OrderId",
                principalTable: "Order",
                principalColumn: "OrderId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Payment_Invoice_InvoiceId",
                table: "Payment",
                column: "InvoiceId",
                principalTable: "Invoice",
                principalColumn: "InvoiceId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Product_ProductSubcategory_SubcategoryId",
                table: "Product",
                column: "SubcategoryId",
                principalTable: "ProductSubcategory",
                principalColumn: "SubcategoryId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSubcategory_ProductCategory_CategoryId",
                table: "ProductSubcategory",
                column: "CategoryId",
                principalTable: "ProductCategory",
                principalColumn: "CategoryId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
