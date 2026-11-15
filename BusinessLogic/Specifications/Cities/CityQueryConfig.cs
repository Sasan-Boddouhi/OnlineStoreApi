// BusinessLogic/Specifications/Cities/CityQueryConfig.cs
using Application.Entities;
using BusinessLogic.DTOs.City;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Cities;

public static class CityQueryConfig
{
    /// <summary>
    /// فیلدهای مجاز فیلتر و مرتب‌سازی. Batch 60 (F-24): <c>province.name</c> و <c>isactive</c> حذف شدند؛
    /// روی <see cref="City"/> وجود ندارند و هر درخواست با آن‌ها به خطا می‌خورد.
    /// </summary>
    public static readonly string[] AllowedFields =
    {
        "cityname",
        "provinceid",
        "province.provincename"
    };

    public static Expression<Func<City, CityDto>> Projection =>
        c => new CityDto
        {
            CityId = c.CityId,
            CityName = c.CityName,
            ProvinceId = c.ProvinceId,
            ProvinceName = c.Province != null ? c.Province.ProvinceName : string.Empty
        };
}