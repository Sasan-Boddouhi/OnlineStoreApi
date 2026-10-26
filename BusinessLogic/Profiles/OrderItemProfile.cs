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
            // Phase 2: این مسیر (OrderItemService) هنوز Listing را ست نمی‌کند؛
            // نوشتن ListingId فقط از مسیر OrderService.AddItemToOrderAsync انجام می‌شود.
            .ForMember(d => d.ListingId, opt => opt.Ignore())
            // Phase 3: گروه فروشنده فقط از مسیر OrderService ساخته می‌شود.
            .ForMember(d => d.OrderSellerGroupId, opt => opt.Ignore())
            // Phase 6: واریانت هم فقط از مسیر OrderService.AddItemToOrderAsync ست می‌شود.
            .ForMember(d => d.VariantId, opt => opt.Ignore());
    }
}
