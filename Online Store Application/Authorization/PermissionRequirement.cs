using Microsoft.AspNetCore.Authorization;

namespace Online_Store_Application.Authorization;

/// <summary>
/// یک نیازمندی مجوززهی مبتنی بر Permission.Code.
/// نام Policyها تغییر نمی‌کند (D8)؛ فقط بدنهٔ آن‌ها به این نیازمندی تغییر می‌کند.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("Permission code is required.", nameof(permission));

        Permission = permission;
    }

    public string Permission { get; }
}

/// <summary>
/// کدهای مجوز. این‌ها قرارداد پایدار بین مایگریشن Seed و کد هستند و هرگز نباید تغییر کنند.
/// </summary>
public static class Permissions
{
    public const string AdminFull = "admin.full";
    public const string CatalogProductsWrite = "catalog.products.write";
    public const string UsersManage = "users.manage";
    public const string EmployeesManage = "employees.manage";
    public const string OrdersRead = "orders.read";
    public const string SellerManage = "seller.manage";
    public const string FinanceRead = "finance.read";
}