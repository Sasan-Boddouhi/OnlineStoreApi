using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_RbacFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    AuditLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.AuditLogId);
                });

            migrationBuilder.CreateTable(
                name: "Permission",
                columns: table => new
                {
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Group = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permission", x => x.PermissionId);
                });

            migrationBuilder.CreateTable(
                name: "Role",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    ModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "RolePermission",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermission", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermission_Permission_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permission",
                        principalColumn: "PermissionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermission_Role_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Role",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRole",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRole", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRole_Role_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Role",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRole_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_OccurredOnUtc",
                table: "AuditLog",
                column: "OccurredOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Permission_Code",
                table: "Permission",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Role_Code",
                table: "Role",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermission_PermissionId",
                table: "RolePermission",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermission_RoleId",
                table: "RolePermission",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_RoleId",
                table: "UserRole",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_UserId",
                table: "UserRole",
                column: "UserId");

            // =====================================================================
            // Phase 1 — Seed داده ثابت (Roles / Permissions / RolePermission)
            // =====================================================================
            var seedTimestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "Role",
                columns: new[] { "RoleId", "Code", "DisplayName", "IsSystem", "IsActive", "CreatedOn", "ModifiedOn" },
                values: new object[,]
                {
                    { 1, "SuperAdmin", "مدیر ارشد", true, true, seedTimestamp, seedTimestamp },
                    { 2, "Admin", "مدیر سیستم", true, true, seedTimestamp, seedTimestamp },
                    { 3, "Manager", "سرپرست", true, true, seedTimestamp, seedTimestamp },
                    { 4, "Seller", "فروشنده", true, true, seedTimestamp, seedTimestamp },
                    { 5, "Customer", "مشتری", true, true, seedTimestamp, seedTimestamp }
                });

            migrationBuilder.InsertData(
                table: "Permission",
                columns: new[] { "PermissionId", "Code", "DisplayName", "Group", "CreatedOn", "ModifiedOn" },
                values: new object[,]
                {
                    { 1, "admin.full", "دسترسی کامل مدیریت", "System", seedTimestamp, seedTimestamp },
                    { 2, "catalog.products.write", "ویرایش کاتالوگ محصولات", "Catalog", seedTimestamp, seedTimestamp },
                    { 3, "users.manage", "مدیریت کاربران", "Users", seedTimestamp, seedTimestamp },
                    { 4, "employees.manage", "مدیریت کارمندان", "Users", seedTimestamp, seedTimestamp },
                    { 5, "orders.read", "مشاهده سفارش‌ها", "Orders", seedTimestamp, seedTimestamp },
                    { 6, "seller.manage", "مدیریت فروشندگان", "Marketplace", seedTimestamp, seedTimestamp },
                    { 7, "finance.read", "مشاهده مالی", "Finance", seedTimestamp, seedTimestamp }
                });

            // SuperAdmin => همه؛ Admin => همه به‌جز seller.manage؛ Manager/Seller/Customer طبق ماتریس.
            // ⚠️ اصلاح نسبت به Spec §۴.۲: Admin مجوز admin.full را هم دارد، وگرنه Policy
            // «AdminOnly» (که به admin.full نگاشت شده) کل دسترسی مدیریتی Admin را قطع می‌کرد.
            migrationBuilder.InsertData(
                table: "RolePermission",
                columns: new[] { "RoleId", "PermissionId" },
                values: new object[,]
                {
                    { 1, 1 }, { 1, 2 }, { 1, 3 }, { 1, 4 }, { 1, 5 }, { 1, 6 }, { 1, 7 },
                    { 2, 1 }, { 2, 2 }, { 2, 3 }, { 2, 4 }, { 2, 5 }, { 2, 7 },
                    { 3, 2 }, { 3, 5 },
                    { 4, 5 }
                });

            // =====================================================================
            // Phase 1 — Backfill پویا (idempotent)
            // =====================================================================

            // گام ۱: هر TypeName موجود در EmployeeType که در Role نیست، نقش می‌شود.
            // این کار از حذف بی‌صدای کاربران دارای EmployeeType غیرمنتظره جلوگیری می‌کند.
            migrationBuilder.Sql(@"
                INSERT INTO [Role] ([Code], [DisplayName], [IsSystem], [IsActive], [CreatedOn], [ModifiedOn])
                SELECT DISTINCT et.[TypeName], et.[TypeName], 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM [EmployeeType] et
                WHERE LEN(LTRIM(RTRIM(et.[TypeName]))) > 0
                  AND NOT EXISTS (SELECT 1 FROM [Role] r WHERE r.[Code] = et.[TypeName]);
            ");

            // گام ۲: نقش هر کارمند از EmployeeType.TypeName (D7: برابری عینی کدها).
            migrationBuilder.Sql(@"
                INSERT INTO [UserRole] ([UserId], [RoleId])
                SELECT e.[UserId], r.[RoleId]
                FROM [Employee] e
                JOIN [EmployeeType] et ON et.[EmployeeTypeId] = e.[EmployeeTypeId]
                JOIN [Role] r ON r.[Code] = et.[TypeName]
                WHERE NOT EXISTS (
                    SELECT 1 FROM [UserRole] ur
                    WHERE ur.[UserId] = e.[UserId] AND ur.[RoleId] = r.[RoleId]);
            ");

            // گام ۳: هر کاربری که هنوز هیچ نقشی ندارد، Customer می‌شود.
            migrationBuilder.Sql(@"
                INSERT INTO [UserRole] ([UserId], [RoleId])
                SELECT u.[UserId], r.[RoleId]
                FROM [User] u
                CROSS JOIN [Role] r
                WHERE r.[Code] = 'Customer'
                  AND NOT EXISTS (SELECT 1 FROM [UserRole] ur WHERE ur.[UserId] = u.[UserId]);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "RolePermission");

            migrationBuilder.DropTable(
                name: "UserRole");

            migrationBuilder.DropTable(
                name: "Permission");

            migrationBuilder.DropTable(
                name: "Role");
        }
    }
}
