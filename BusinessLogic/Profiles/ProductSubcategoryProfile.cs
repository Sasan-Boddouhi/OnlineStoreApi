using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class ProductSubcategoryProfile : Profile
{
    public ProductSubcategoryProfile()
    {
        CreateMap<ProductSubcategory, ProductSubcategoryDto>()
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category != null ? src.Category.CategoryName : null));

        CreateMap<CreateProductSubcategoryDto, ProductSubcategory>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.SubcategoryId, opt => opt.Ignore())
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true));

        CreateMap<ProductSubcategory, UpdateProductSubcategoryDto>()
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category != null ? src.Category.CategoryName : null));

        CreateMap<UpdateProductSubcategoryDto, ProductSubcategory>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.IsActive, opt => opt.Ignore());
    }
}
