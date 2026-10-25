using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Address;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class AddressProfile : Profile
{
    public AddressProfile()
    {
        CreateMap<CreateAddressDto, Address>()
            .ConfigureDbDestination()
            .ForMember(d => d.AddressId, opt => opt.Ignore())
            .ForMember(d => d.UserId, opt => opt.Ignore()); // فقط شناسه‌های اصلی که کلاس یا لیست نیستند باقی می‌مانند

        CreateMap<Address, AddressDto>()
            .ForMember(d => d.CityName, opt => opt.MapFrom(s => s.City != null ? s.City.CityName : null));
    }
}
