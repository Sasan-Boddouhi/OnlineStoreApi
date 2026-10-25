using Application.Entities;
using AutoMapper;
using BusinessLogic.Common.Mapping;
using BusinessLogic.DTOs.Listing;

namespace BusinessLogic.Profiles;

public class SellerProductProfile : Profile
{
    public SellerProductProfile()
    {
        CreateMap<SellerProduct, SellerProductDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()))
            // Phase 6: ProductId از مسیر واریانت استخراج می‌شود.
            .ForMember(dest => dest.ProductId, opt =>
                opt.MapFrom(src => src.Variant != null ? src.Variant.ProductId : 0));

        CreateMap<CreateSellerProductDto, SellerProduct>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.ListingId, opt => opt.Ignore());

        CreateMap<UpdateSellerProductDto, SellerProduct>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.ListingId, opt => opt.Ignore())
            .ForMember(dest => dest.SellerId, opt => opt.Ignore())
            .ForMember(dest => dest.VariantId, opt => opt.Ignore());
    }
}