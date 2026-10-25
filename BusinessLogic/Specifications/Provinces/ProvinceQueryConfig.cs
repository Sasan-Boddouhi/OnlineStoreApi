using Application.Entities;
using BusinessLogic.DTOs.Province;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Provinces;

public static class ProvinceQueryConfig
{
    public static readonly string[] AllowedFields =
    {
        "provincename",
        "isactive"
    };

    public static Expression<Func<Province, ProvinceDto>> Projection =>
        p => new ProvinceDto
        {
            ProvinceId = p.ProvinceId,
            ProvinceName = p.ProvinceName
        };
}