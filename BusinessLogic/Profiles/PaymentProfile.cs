using Application.DTOs.Order;
using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Payment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Profiles
{
    public class PaymentProfile : Profile
    {
        public PaymentProfile()
        {
            CreateMap<Payment, PaymentDto>();
        }
    }
}
