using Application.Entities;
using Application.Interfaces.Security;
using DataLayer.Context;
using DataLayer.Security;
using Microsoft.EntityFrameworkCore;

namespace OnlineStore.Tests.Integration.Infrastructure;

public static class TestDataSeed
{
    // این داده‌ها آینهٔ دقیق Seed مایگریشن Phase1_RbacFoundation هستند.
    private static readonly (string Code, string DisplayName, string Group)[] PermissionSeed =
    {
        ("admin.full", "دسترسی کامل مدیریت", "System"),
        ("catalog.products.write", "ویرایش کاتالوگ محصولات", "Catalog"),
        ("users.manage", "مدیریت کاربران", "Users"),
        ("employees.manage", "مدیریت کارمندان", "Users"),
        ("orders.read", "مشاهده سفارش‌ها", "Orders"),
        ("seller.manage", "مدیریت فروشندگان", "Marketplace"),
        ("finance.read", "مشاهده مالی", "Finance")
    };

    private static readonly (string Code, string DisplayName)[] RoleSeed =
    {
        ("SuperAdmin", "مدیر ارشد"),
        ("Admin", "مدیر سیستم"),
        ("Manager", "سرپرست"),
        ("Seller", "فروشنده"),
        ("Customer", "مشتری")
    };

    private static readonly Dictionary<string, string[]> RolePermissionSeed = new()
    {
        ["SuperAdmin"] = PermissionSeed.Select(p => p.Code).ToArray(),
        // اصلاح Spec: ماتریس §۴.۲ مجوز admin.full را فقط به SuperAdmin می‌داد،
        // در حالی که Policy «AdminOnly» در §۴.۵ دقیقاً به admin.full نگاشت شده است.
        // نتیجه، حذف کامل دسترسی Admin به تمام endpointهای مدیریتی بود (۲۸ تست ۴۰۳).
        // برای حفظ «صفر تغییر» در رفتار قبلی (D8)، Admin هم admin.full می‌گیرد.
        ["Admin"] = new[]
        {
            "admin.full", "catalog.products.write", "users.manage", "employees.manage", "orders.read", "finance.read"
        },
        ["Manager"] = new[] { "catalog.products.write", "orders.read" },
        ["Seller"] = new[] { "orders.read" },
        ["Customer"] = Array.Empty<string>()
    };

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher? passwordHasher = null)
    {
        passwordHasher ??= new BcryptPasswordHasher();

        // ۰. RBAC (Phase 1) — قبل از هر چیز، تا نقش قابل انتساب باشد.
        var roles = await SeedRbacAsync(db);

        // ۱. نوع کارمند Admin
        var adminType = await db.EmployeeType
            .FirstOrDefaultAsync(et => et.TypeName == "Admin");
        if (adminType == null)
        {
            adminType = new EmployeeType
            {
                TypeName = "Admin",
                DisplayName = "مدیر سیستم"
            };
            db.EmployeeType.Add(adminType);
            await db.SaveChangesAsync();
        }

        // ۲. کاربر ادمین
        if (!await db.User.AnyAsync(u => u.PhoneNumber == "09123456789"))
        {
            var user = new User
            {
                PhoneNumber = "09123456789",
                PasswordHash = passwordHasher.Hash("Test@123"),
                FirstName = "ادمین",
                LastName = "تست",
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(),
                UserType = UserType.Employee
            };
            db.User.Add(user);
            await db.SaveChangesAsync();

            db.Employee.Add(new Employee
            {
                UserId = user.UserId,
                EmployeeTypeId = adminType.EmployeeTypeId,
                EmployeeNumber = "E-001"
            });
            await db.SaveChangesAsync();
        }

        // ۳. انتساب نقش Admin به کاربر ادمین (Phase 1)
        var adminUser = await db.User.FirstOrDefaultAsync(u => u.PhoneNumber == "09123456789");
        if (adminUser != null)
        {
            var adminRole = roles["Admin"];
            var alreadyLinked = await db.UserRole
                .AnyAsync(ur => ur.UserId == adminUser.UserId && ur.RoleId == adminRole.RoleId);

            if (!alreadyLinked)
            {
                db.UserRole.Add(new UserRole
                {
                    UserId = adminUser.UserId,
                    RoleId = adminRole.RoleId
                });
                await db.SaveChangesAsync();
            }
        }

        // ۴. دسته‌بندی‌ها
        if (!await db.ProductCategory.AnyAsync())
        {
            db.ProductCategory.Add(new ProductCategory { CategoryName = "Test Category" });
            await db.SaveChangesAsync();
        }

        var category = await db.ProductCategory.FirstAsync();

        if (!await db.ProductSubcategory.AnyAsync())
        {
            db.ProductSubcategory.Add(new ProductSubcategory
            {
                SubcategoryName = "Test Subcategory",
                CategoryId = category.CategoryId
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<Dictionary<string, Role>> SeedRbacAsync(AppDbContext db)
    {
        var roles = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, displayName) in RoleSeed)
        {
            var role = await db.Role.FirstOrDefaultAsync(r => r.Code == code);
            if (role is null)
            {
                role = new Role { Code = code, DisplayName = displayName, IsSystem = true };
                db.Role.Add(role);
            }
            roles[code] = role;
        }
        await db.SaveChangesAsync();

        var permissions = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, displayName, group) in PermissionSeed)
        {
            var permission = await db.Permission.FirstOrDefaultAsync(p => p.Code == code);
            if (permission is null)
            {
                permission = new Permission { Code = code, DisplayName = displayName, Group = group };
                db.Permission.Add(permission);
            }
            permissions[code] = permission;
        }
        await db.SaveChangesAsync();

        foreach (var (roleCode, permissionCodes) in RolePermissionSeed)
        {
            var role = roles[roleCode];

            foreach (var permissionCode in permissionCodes)
            {
                var permission = permissions[permissionCode];
                var exists = await db.RolePermission
                    .AnyAsync(rp => rp.RoleId == role.RoleId && rp.PermissionId == permission.PermissionId);

                if (!exists)
                {
                    db.RolePermission.Add(new RolePermission
                    {
                        RoleId = role.RoleId,
                        PermissionId = permission.PermissionId
                    });
                }
            }
        }
        await db.SaveChangesAsync();

        return roles;
    }
}