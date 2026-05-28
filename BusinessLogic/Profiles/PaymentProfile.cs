using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class PaymentProfile : Profile
{
    public PaymentProfile()
    {
        // ۱. موجودیت به دی‌تی‌او خروجی
        CreateMap<Payment, PaymentDto>();

        // ۲. ساخت تراکنش جدید (اضافه شدن خط IsActive برای رفع خطای لاگ)
        CreateMap<CreatePaymentDto, Payment>()
            .ConfigureDbDestination()
            .ForMember(d => d.PaymentId, opt => opt.Ignore())
            .ForMember(d => d.IsActive, opt => opt.MapFrom(_ => true)); // تنظیم مقدار پیش‌فرض فعال

        // ۳. ویرایش تراکنش
        CreateMap<UpdatePaymentDto, Payment>()
            .ConfigureDbDestination()
            .ForMember(d => d.PaymentId, opt => opt.Ignore())
            .ForAllMembers(opts =>
                opts.Condition((src, dest, srcMember) => srcMember != null));
    }
}
