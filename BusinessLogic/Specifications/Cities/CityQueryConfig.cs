// BusinessLogic/Specifications/Cities/CityQueryConfig.cs
using Application.Entities;
using BusinessLogic.DTOs.City;
using System;
using System.Linq.Expressions;

namespace BusinessLogic.Specifications.Cities;

public static class CityQueryConfig
{
    public static readonly string[] AllowedFields =
    {
        "cityname",
        "province.name",
        "isactive"
    };

    public static Expression<Func<City, CityDto>> Projection =>
        c => new CityDto
        {
            CityId = c.CityId,
            CityName = c.CityName,
            ProvinceId = c.ProvinceId,
            ProvinceName = c.Province != null ? c.Province.ProvinceName : null
        };
}