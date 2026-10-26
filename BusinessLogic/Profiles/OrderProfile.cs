using System.Linq;
using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Order;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class OrderProfile : Profile
{
    public OrderProfile()
    {
        CreateMap<Order, OrderDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()));

        CreateMap<OrderDto, Order>()
            .ConfigureDbDestination()
            .ForMember(d => d.CustomerId, opt => opt.Ignore());

        CreateMap<Order, OrderDetailsDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()))
            .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.OrderItems))
            .ForMember(dest => dest.InvoiceNumber, opt => opt.MapFrom(src => src.Invoice != null ? src.Invoice.InvoiceNumber : null))
            .ForMember(dest => dest.IsPaid, opt => opt.MapFrom(src => src.Invoice != null && src.Invoice.Payments != null && src.Invoice.Payments.Any(p => p.State == PaymentState.Succeeded)));

        CreateMap<OrderDetailsDto, Order>()
            .ConfigureDbDestination()
            .ForMember(d => d.CustomerId, opt => opt.Ignore());
    }
}
