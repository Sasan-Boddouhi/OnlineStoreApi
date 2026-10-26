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
    }
}
