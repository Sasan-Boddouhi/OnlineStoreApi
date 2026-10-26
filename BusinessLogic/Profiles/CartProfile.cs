using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class CartProfile : Profile
{
    public CartProfile()
    {
        CreateMap<Cart, CartDto>()
            .ForMember(d => d.TotalItems, opt => opt.MapFrom(s => s.Items.Count))
            .ForMember(d => d.TotalAmount, opt => opt.MapFrom(s => s.TotalAmount))
            .ForMember(d => d.IsAnonymous, opt => opt.MapFrom(s => s.IsAnonymous));

        CreateMap<CartItem, CartItemDto>()
            .ForMember(d => d.SellerId, opt => opt.MapFrom(s => s.Listing != null ? s.Listing.SellerId : 0))
            .ForMember(d => d.SellerStoreName, opt => opt.MapFrom(s => s.Listing != null && s.Listing.Seller != null ? s.Listing.Seller.StoreName : string.Empty))
            .ForMember(d => d.ProductName, opt => opt.MapFrom(s => s.Listing != null && s.Listing.Variant != null && s.Listing.Variant.Product != null ? s.Listing.Variant.Product.Name : string.Empty))
            .ForMember(d => d.VariantName, opt => opt.MapFrom(s => s.Variant != null ? s.Variant.Name : (s.Listing != null && s.Listing.Variant != null ? s.Listing.Variant.Name : null)))
            .ForMember(d => d.LineTotal, opt => opt.MapFrom(s => s.LineTotal));

        CreateMap<AddCartItemDto, CartItem>()
            .ConfigureDbDestination()
            .ForMember(d => d.CartItemId, opt => opt.Ignore())
            .ForMember(d => d.CartId, opt => opt.Ignore())
            .ForMember(d => d.Cart, opt => opt.Ignore())
            .ForMember(d => d.Listing, opt => opt.Ignore())
            .ForMember(d => d.VariantId, opt => opt.Ignore())
            .ForMember(d => d.Variant, opt => opt.Ignore())
            .ForMember(d => d.UnitPrice, opt => opt.Ignore())
            .ForMember(d => d.AddedAt, opt => opt.Ignore());

        CreateMap<UpdateCartItemDto, CartItem>()
            .ConfigureDbDestination()
            .ForMember(d => d.CartItemId, opt => opt.Ignore())
            .ForMember(d => d.CartId, opt => opt.Ignore())
            .ForMember(d => d.Cart, opt => opt.Ignore())
            .ForMember(d => d.ListingId, opt => opt.Ignore())
            .ForMember(d => d.Listing, opt => opt.Ignore())
            .ForMember(d => d.VariantId, opt => opt.Ignore())
            .ForMember(d => d.Variant, opt => opt.Ignore())
            .ForMember(d => d.UnitPrice, opt => opt.Ignore())
            .ForMember(d => d.AddedAt, opt => opt.Ignore());
    }
}
