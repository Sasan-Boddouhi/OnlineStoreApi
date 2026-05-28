using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Province;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class ProvinceProfile : Profile
{
    public ProvinceProfile()
    {
        CreateMap<Province, ProvinceDto>();

        CreateMap<CreateProvinceDto, Province>()
            .ConfigureDbDestination()
            .ForMember(d => d.ProvinceId, opt => opt.Ignore());

        CreateMap<Province, UpdateProvinceDto>();

        CreateMap<UpdateProvinceDto, Province>()
            .ConfigureDbDestination();
    }
}
