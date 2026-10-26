using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Review;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class ReviewProfile : Profile
{
    public ReviewProfile()
    {
        CreateMap<ProductReview, ProductReviewDto>()
            .ForMember(d => d.CustomerName, opt => opt.MapFrom(s => s.Customer != null && s.Customer.User != null ? (s.Customer.User.FirstName + " " + s.Customer.User.LastName).Trim() : string.Empty));

        CreateMap<SellerReview, SellerReviewDto>()
            .ForMember(d => d.CustomerName, opt => opt.MapFrom(s => s.Customer != null && s.Customer.User != null ? (s.Customer.User.FirstName + " " + s.Customer.User.LastName).Trim() : string.Empty));

        CreateMap<CreateProductReviewDto, ProductReview>()
            .ConfigureDbDestination()
            .ForMember(d => d.ProductReviewId, opt => opt.Ignore())
            .ForMember(d => d.ProductId, opt => opt.Ignore())
            .ForMember(d => d.Product, opt => opt.Ignore())
            .ForMember(d => d.CustomerId, opt => opt.Ignore())
            .ForMember(d => d.Customer, opt => opt.Ignore())
            .ForMember(d => d.OrderItemId, opt => opt.Ignore())
            .ForMember(d => d.OrderItem, opt => opt.Ignore())
            .ForMember(d => d.IsApproved, opt => opt.MapFrom(_ => false));

        CreateMap<CreateSellerReviewDto, SellerReview>()
            .ConfigureDbDestination()
            .ForMember(d => d.SellerReviewId, opt => opt.Ignore())
            .ForMember(d => d.SellerId, opt => opt.Ignore())
            .ForMember(d => d.Seller, opt => opt.Ignore())
            .ForMember(d => d.CustomerId, opt => opt.Ignore())
            .ForMember(d => d.Customer, opt => opt.Ignore())
            .ForMember(d => d.IsApproved, opt => opt.MapFrom(_ => false));
    }
}
