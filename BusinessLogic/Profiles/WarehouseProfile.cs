using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Warehouse;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class WarehouseProfile : Profile
{
    public WarehouseProfile()
    {
        CreateMap<CreateWarehouseDto, Warehouse>()
            .ConfigureDbDestination()
            .ForMember(d => d.WarehouseId, opt => opt.Ignore());

        CreateMap<UpdateWarehouseDto, Warehouse>()
            .ConfigureDbDestination()
            .ForMember(d => d.WarehouseId, opt => opt.Ignore());

        CreateMap<Warehouse, WarehouseDto>()
            .ForMember(d => d.AddressName, opt => opt.Ignore()) // Set manually
            .ForMember(d => d.SellerName, opt => opt.Ignore()); // Set manually
    }
}