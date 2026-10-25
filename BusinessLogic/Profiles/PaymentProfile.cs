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
            .ForMember(d => d.IsActive, opt => opt.MapFrom(_ => true)) // تنظیم مقدار پیش‌فرض فعال
            // Phase 8: این‌ها فقط از مسیر ماشین حالت پر می‌شوند، نه از DTO.
            .ForMember(d => d.State, opt => opt.Ignore())
            .ForMember(d => d.Gateway, opt => opt.Ignore())
            .ForMember(d => d.LastStateChangedAtUtc, opt => opt.Ignore())
            .ForMember(d => d.IdempotencyKey, opt => opt.Ignore())
            .ForMember(d => d.OrderId, opt => opt.Ignore())
            .ForMember(d => d.GatewayReference, opt => opt.Ignore());

        // ۳. ویرایش تراکنش
        CreateMap<UpdatePaymentDto, Payment>()
            .ConfigureDbDestination()
            .ForMember(d => d.PaymentId, opt => opt.Ignore())
            // Phase 8: وضعیت فقط با TransitionTo تغییر می‌کند، نه با Update DTO.
            .ForMember(d => d.State, opt => opt.Ignore())
            .ForMember(d => d.Gateway, opt => opt.Ignore())
            .ForMember(d => d.LastStateChangedAtUtc, opt => opt.Ignore())
            .ForMember(d => d.IdempotencyKey, opt => opt.Ignore())
            .ForMember(d => d.OrderId, opt => opt.Ignore())
            .ForMember(d => d.GatewayReference, opt => opt.Ignore())
            .ForAllMembers(opts =>
                opts.Condition((src, dest, srcMember) => srcMember != null));
    }
}
