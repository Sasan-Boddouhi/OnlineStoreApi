using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Listing;

namespace BusinessLogic.Profiles;

public class InventoryProfile : Profile
{
    public InventoryProfile()
    {
        CreateMap<Inventory, InventoryStockDto>()
            .ForMember(d => d.AddedQuantity, opt => opt.MapFrom(s => s.Quantity))
            .ForMember(d => d.AvailableQuantity, opt => opt.Ignore());
    }
}
