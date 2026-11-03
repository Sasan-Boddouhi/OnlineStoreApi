using Application.Entities;
using AutoMapper;
using BusinessLogic.Common.Mapping;
using BusinessLogic.DTOs.Brand;
using BusinessLogic.DTOs.Coupon;
using BusinessLogic.DTOs.Shipping;

namespace BusinessLogic.Profiles;

public class CommerceProfile : Profile
{
    public CommerceProfile()
    {
        CreateMap<ShippingMethod, ShippingMethodDto>();
        CreateMap<CreateShippingMethodDto, ShippingMethod>()
            .ConfigureDbDestination()
            .ForMember(d => d.ShippingMethodId, opt => opt.Ignore());
        CreateMap<UpdateShippingMethodDto, ShippingMethod>()
            .ConfigureDbDestination()
            .ForMember(d => d.ShippingMethodId, opt => opt.Ignore());

        CreateMap<Coupon, CouponDto>();
        CreateMap<CreateCouponDto, Coupon>()
            .ConfigureDbDestination()
            .ForMember(d => d.CouponId, opt => opt.Ignore());
        CreateMap<UpdateCouponDto, Coupon>()
            .ConfigureDbDestination()
            .ForMember(d => d.CouponId, opt => opt.Ignore());

        CreateMap<Brand, BrandDto>();
        CreateMap<CreateBrandDto, Brand>()
            .ConfigureDbDestination()
            .ForMember(d => d.BrandId, opt => opt.Ignore())
            .ForMember(d => d.Products, opt => opt.Ignore());
        CreateMap<UpdateBrandDto, Brand>()
            .ConfigureDbDestination()
            .ForMember(d => d.BrandId, opt => opt.Ignore())
            .ForMember(d => d.Products, opt => opt.Ignore());
    }
}
