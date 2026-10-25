using Application.Entities;
using AutoMapper;
using BusinessLogic.Common.Mapping;
using BusinessLogic.DTOs.Seller;

namespace BusinessLogic.Profiles;

public class SellerProfile : Profile
{
    public SellerProfile()
    {
        CreateMap<Seller, SellerDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()));

        // Phase 9: AverageRating و ReviewCount از ستون‌های کش‌شدهٔ خود موجودیت map می‌شوند.
        // Status/IsActive عمداً از ورودی کاربر خوانده نمی‌شوند:
        // فروشنده جدید همیشه Pending و فعال است و تأیید فقط از مسیر Update انجام می‌شود.
        CreateMap<CreateSellerDto, Seller>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.SellerId, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.IsActive, opt => opt.Ignore())
            // Phase 9 (R33): کش امتیاز ورودی کاربر نیست.
            .ForMember(dest => dest.AverageRating, opt => opt.Ignore())
            .ForMember(dest => dest.ReviewCount, opt => opt.Ignore())
            // Phase 3: نرخ کمیسیون در زمان ثبت، پیش‌فرض پلتفرم است (null).
            .ForMember(dest => dest.CommissionRate, opt => opt.Ignore());

        CreateMap<UpdateSellerDto, Seller>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.SellerId, opt => opt.Ignore())
            .ForMember(dest => dest.UserId, opt => opt.Ignore())
            .ForMember(dest => dest.CommissionRate, opt =>
            {
                opt.PreCondition(src => src.CommissionRate.HasValue);
                opt.MapFrom(src => src.CommissionRate!.Value);
            })
            // Phase 9 (R33): کش امتیاز از نظرها محاسبه می‌شود، نه از DTO.
            .ForMember(dest => dest.AverageRating, opt => opt.Ignore())
            .ForMember(dest => dest.ReviewCount, opt => opt.Ignore());
    }
}