using Application.Entities;
using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Profiles
{
    public class ProductProfile : Profile
    {
        public ProductProfile()
        {
            //CreateMap<Product, ProductDto>()
            //    .ForMember(dest => dest.SubcategoryName, opt => opt.MapFrom(src => src.Subcategory.SubcategoryName))
            //    .ForMember(dest => dest.CategoryId, opt => opt.MapFrom(src => src.Subcategory.CategoryId))
            //    .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Subcategory.Category.CategoryName));

            //CreateMap<CreateProductDto, Product>()
            //    .ForMember(dest => dest.SubcategoryId, opt => opt.MapFrom(src => src.ProductSubcategoryId));


            //CreateMap<UpdateProductDto, Product>().ReverseMap();
        }
    }
}
