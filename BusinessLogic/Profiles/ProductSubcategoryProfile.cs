using Application.Entities;
using AutoMapper;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Profiles
{   
    public class ProductSubcategoryProfile : Profile
    {
        public ProductSubcategoryProfile()
        {
            // ProductSubcategory -> ProductSubcategoryDto (برای نمایش)
            //CreateMap<ProductSubcategory, ProductSubcategoryDto>()
            //    .ForMember(dest => dest.ProductCategoryId, opt => opt.MapFrom(src => src.CategoryId))
            //    .ForMember(dest => dest.ProductSubcategoryId, opt => opt.MapFrom(src => src.SubcategoryId))
            //    .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.SubcategoryName))
            //    .ForMember(dest => dest.ProductCategoryName, opt => opt.MapFrom(src => src.Category.CategoryName));

            //CreateMap<CreateProductSubcategoryDto, ProductSubcategory>()
            //    .ForMember(dest => dest.SubcategoryName, opt => opt.MapFrom(src => src.Name))
            //    .ForMember(dest => dest.CategoryId, opt => opt.MapFrom(src => src.ProductCategoryId))
            //    .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true));

            //CreateMap<ProductSubcategory, UpdateProductSubcategoryDto>()
            //    .ForMember(dest => dest.ProductSubcategoryId, opt => opt.MapFrom(src => src.SubcategoryId))
            //    .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.SubcategoryName))
            //    .ForMember(dest => dest.ProductCategoryId, opt => opt.MapFrom(src => src.CategoryId))
            //    .ForMember(dest => dest.ProductCategoryName, opt => opt.MapFrom(src => src.Category.CategoryName))
            //    .ReverseMap();

        }
    }
}
