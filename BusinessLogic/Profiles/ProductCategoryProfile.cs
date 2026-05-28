using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.ProductCategory;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class ProductCategoryProfile : Profile
{
    public ProductCategoryProfile()
    {
        CreateMap<ProductCategory, ProductCategoryDto>()
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.CategoryName))
            .ForMember(dest => dest.ProductCategoryId, opt => opt.MapFrom(src => src.CategoryId));

        CreateMap<CreateProductCategoryDto, ProductCategory>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.CategoryId, opt => opt.Ignore())
            .ForMember(dest => dest.Description, opt => opt.Ignore())
            .ForMember(dest => dest.IsActive, opt => opt.Ignore())
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Name));

        CreateMap<UpdateProductCategoryDto, ProductCategory>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.Description, opt => opt.Ignore())
            .ForMember(dest => dest.IsActive, opt => opt.Ignore())
            .ForMember(dest => dest.CategoryId, opt => opt.MapFrom(src => src.ProductCategoryId))
            .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Name));

        CreateMap<ProductCategoryDto, UpdateProductCategoryDto>().ReverseMap();
    }
}
