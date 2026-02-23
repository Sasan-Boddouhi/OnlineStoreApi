using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Address;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Profiles
{
    public class AddressProfile : Profile
    {
        public AddressProfile()
        {
            //CreateMap<Address, AddressDto>()
            //    .ForMember(dest => dest.CityName, opt => opt.MapFrom(src => src.City.CityName))
            //    .ForMember(dest => dest.ProvinceName, opt => opt.MapFrom(src => src.City.Province.ProvinceName));

            CreateMap<CreateAddressDto, Address>()
                .ForMember(dest => dest.AddressId, opt => opt.Ignore())
                .ForMember(dest => dest.UserId, opt => opt.Ignore())
                .ForMember(dest => dest.User, opt => opt.Ignore())
                .ForMember(dest => dest.Warehouses, opt => opt.Ignore());

            //CreateMap<UpdateAddressDto, Address>();

            //CreateMap<CreateAddressDto, AddressDto>();
            //CreateMap<UpdateAddressDto, AddressDto>();
        }
    }
}
