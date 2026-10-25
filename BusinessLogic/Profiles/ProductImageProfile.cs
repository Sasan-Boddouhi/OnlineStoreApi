using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Media;

namespace BusinessLogic.Profiles;

/// <summary>Phase 5 — نگاشت گالری محصول.</summary>
public class ProductImageProfile : Profile
{
    public ProductImageProfile()
    {
        CreateMap<ProductImage, ProductImageDto>();
    }
}