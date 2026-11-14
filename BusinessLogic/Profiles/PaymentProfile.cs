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

        // Batch 42 (FIN-12): ردیف صف بررسی دستی (فقط ادمین).
        CreateMap<Payment, PaymentReviewDto>()
            .ForMember(d => d.ReviewRequiredAtUtc, o => o.MapFrom(s => s.ReviewRequiredAtUtc ?? default));
    }
}
