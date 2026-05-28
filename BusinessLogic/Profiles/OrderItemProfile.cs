using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class OrderItemProfile : Profile
{
    public OrderItemProfile()
    {
        CreateMap<OrderItem, OrderItemDto>();

        CreateMap<CreateOrderItemDto, OrderItem>()
            .ConfigureDbDestination()
            .ForMember(d => d.OrderItemId, opt => opt.Ignore())
            .ForMember(d => d.OrderId, opt => opt.Ignore()) 
            .ForMember(d => d.TotalPrice, opt => opt.Ignore())
            .ForMember(d => d.IsActive, opt => opt.Ignore());
    }
}
