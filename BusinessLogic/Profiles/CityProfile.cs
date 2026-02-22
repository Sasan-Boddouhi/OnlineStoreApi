using Application.Entities;
using AutoMapper;

namespace BusinessLogic.Profiles
{
    public class CityProfile : Profile
    {
        public CityProfile()
        {

            //CreateMap<City, CityDto>()
            //    .ForMember(dest => dest.ProvinceName, opt => opt.MapFrom(src => src.Province.ProvinceName));

            //CreateMap<CreateCityDto, City>();

            //CreateMap<City, UpdateCityDto>()
            //    .ReverseMap();
        }
    }
}
