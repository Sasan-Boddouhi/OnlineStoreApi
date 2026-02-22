using Application.Entities;
using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Profiles
{
    public class ProductCategoryProfile : Profile
    {
        public ProductCategoryProfile()
        {
            //CreateMap<ProductCategory, ProductCategoryDto>()
            //    .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.CategoryName))
            //    .ForMember(dest => dest.ProductCategoryId, opt => opt.MapFrom(src => src.CategoryId));

            //CreateMap<CreateProductCategoryDto, ProductCategory>()
            //    .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Name));

            //CreateMap<UpdateProductCategoryDto, ProductCategory>()
            //    .ForMember(dest => dest.CategoryId, opt => opt.MapFrom(src => src.ProductCategoryId))
            //    .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Name))
            //    .ReverseMap();

            //CreateMap<ProductCategoryDto, UpdateProductCategoryDto>().ReverseMap();
        }
    }
}
