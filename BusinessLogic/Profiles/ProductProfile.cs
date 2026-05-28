using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class ProductProfile : Profile
{
    public ProductProfile()
    {
        CreateMap<Product, ProductDto>()
            .ForMember(dest => dest.SubcategoryName,
                opt => opt.MapFrom(src => src.Subcategory != null ? src.Subcategory.SubcategoryName : null))
            .ForMember(dest => dest.CategoryId,
                opt => opt.MapFrom(src => src.Subcategory != null ? src.Subcategory.CategoryId : 0))
            .ForMember(dest => dest.CategoryName,
                opt => opt.MapFrom(src => src.Subcategory != null && src.Subcategory.Category != null
                    ? src.Subcategory.Category.CategoryName
                    : null));

        CreateMap<CreateProductDto, Product>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.ProductId, opt => opt.Ignore())
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true))
            .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
            .ForMember(dest => dest.Unit, opt => opt.Ignore())
            .ForMember(dest => dest.Barcode, opt => opt.Ignore())
            .ForMember(dest => dest.ImageUrl, opt => opt.Ignore())
            .ForMember(dest => dest.ExpirationDate, opt => opt.Ignore());

        CreateMap<UpdateProductDto, Product>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.IsActive, opt => opt.Ignore())
            .ForMember(dest => dest.Unit, opt => opt.Ignore())
            .ForMember(dest => dest.Barcode, opt => opt.Ignore())
            .ForMember(dest => dest.ImageUrl, opt => opt.Ignore())
            .ForMember(dest => dest.ExpirationDate, opt => opt.Ignore());
    }
}
