using System.Reflection;
using Application.Entities;
using BusinessLogic.Specifications.Cities;
using BusinessLogic.Specifications.Employees;
using BusinessLogic.Specifications.EmployeeTypes;
using BusinessLogic.Specifications.ProductCategories;
using BusinessLogic.Specifications.Products;
using BusinessLogic.Specifications.ProductSubcategories;
using BusinessLogic.Specifications.Provinces;
using BusinessLogic.Specifications.Users;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Queries;

/// <summary>
/// Batch 60 (F-24): هر فیلدی که در فهرست مجاز فیلتر/مرتب‌سازی آمده باید واقعاً روی موجودیت وجود داشته باشد؛
/// پیش از این <c>province.name</c> و <c>isactive</c> (شهر، استان) و <c>employeetype.typename</c> (کاربر)
/// مجاز اعلام شده بودند ولی وجود نداشتند و هر درخواست با آن‌ها به خطای داخلی می‌خورد.
/// </summary>
public class QueryConfigWhitelistTests
{
    public static IEnumerable<object[]> Configs()
    {
        yield return new object[] { typeof(City), CityQueryConfig.AllowedFields };
        yield return new object[] { typeof(Province), ProvinceQueryConfig.AllowedFields };
        yield return new object[] { typeof(User), UserQueryConfig.AllowedFields };
        yield return new object[] { typeof(Product), ProductQueryConfig.AllowedFields };
        yield return new object[] { typeof(ProductCategory), ProductCategoryQueryConfig.AllowedFields };
        yield return new object[] { typeof(ProductSubcategory), ProductSubcategoryQueryConfig.AllowedFields };
        yield return new object[] { typeof(Employee), EmployeeQueryConfig.AllowedFields };
        yield return new object[] { typeof(EmployeeType), EmployeeTypeQueryConfig.AllowedFields };
    }

    [Theory]
    [MemberData(nameof(Configs))]
    public void EveryWhitelistedField_ExistsOnTheEntity(Type entity, string[] allowedFields)
    {
        var missing = allowedFields.Where(f => !Resolves(entity, f)).ToList();

        missing.Should().BeEmpty($"every whitelisted field of {entity.Name} must be a real property path");
    }

    private static bool Resolves(Type type, string path)
    {
        var current = type;
        foreach (var segment in path.Split('.'))
        {
            var property = current.GetProperty(segment,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null)
                return false;
            current = property.PropertyType;
        }
        return true;
    }
}
