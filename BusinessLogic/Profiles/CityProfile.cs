using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.City;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class CityProfile : Profile
{
    public CityProfile()
    {
        CreateMap<City, CityDto>()
            .ForMember(dest => dest.ProvinceName, opt => opt.MapFrom(src => src.Province != null ? src.Province.ProvinceName : null));

        CreateMap<CreateCityDto, City>()
            .ConfigureDbDestination()
            .ForMember(d => d.CityId, opt => opt.Ignore());

        CreateMap<City, UpdateCityDto>();

        CreateMap<UpdateCityDto, City>()
            .ConfigureDbDestination()
            .ForMember(d => d.ProvinceId, opt => opt.Ignore());
    }
}
