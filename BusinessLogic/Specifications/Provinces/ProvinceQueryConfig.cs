using Application.Entities;
using BusinessLogic.DTOs.Province;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Provinces;

public static class ProvinceQueryConfig
{
    /// <summary>
    /// فیلدهای مجاز فیلتر و مرتب‌سازی. Batch 60 (F-24): <c>isactive</c> حذف شد؛ روی <see cref="Province"/> وجود ندارد.
    /// </summary>
    public static readonly string[] AllowedFields =
    {
        "provincename"
    };

    public static Expression<Func<Province, ProvinceDto>> Projection =>
        p => new ProvinceDto
        {
            ProvinceId = p.ProvinceId,
            ProvinceName = p.ProvinceName
        };
}