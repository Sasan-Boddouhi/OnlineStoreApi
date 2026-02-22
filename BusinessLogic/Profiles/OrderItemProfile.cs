using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.OrderItem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Profiles
{
    public class OrderItemProfile : Profile
    {
        public OrderItemProfile()
        {
            CreateMap<OrderItem, OrderItemDto>();
                
            CreateMap<CreateOrderItemDto, OrderItem>();
        }
    }
}
