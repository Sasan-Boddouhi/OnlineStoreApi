using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class PaymentProfile : Profile
{
    public PaymentProfile()
    {
        // موجودیت به دی‌تی‌او خروجی
        CreateMap<Payment, PaymentDto>();
    }
}
