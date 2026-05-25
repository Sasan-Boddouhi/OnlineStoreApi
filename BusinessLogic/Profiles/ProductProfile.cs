using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Product;
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
            CreateMap<Product, ProductDto>()
                        .ForMember(dest => dest.SubcategoryName,
                            opt => opt.MapFrom(src => src.Subcategory != null ? src.Subcategory.SubcategoryName : null))
                        .ForMember(dest => dest.CategoryId,
                            opt => opt.MapFrom(src => src.Subcategory != null ? src.Subcategory.CategoryId : 0))
                        .ForMember(dest => dest.CategoryName,
                            opt => opt.MapFrom(src => src.Subcategory != null && src.Subcategory.Category != null
                                ? src.Subcategory.Category.CategoryName
                                : null))
                        .ForMember(dest => dest.Barcode, opt => opt.MapFrom(src => src.Barcode))
                        .ForMember(dest => dest.SubcategoryId, opt => opt.MapFrom(src => src.SubcategoryId))
                        .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                        .ForMember(dest => dest.ExpirationDate, opt => opt.MapFrom(src => src.ExpirationDate));

            CreateMap<CreateProductDto, Product>()
                .ForMember(dest => dest.Subcategory, opt => opt.Ignore())
                .ForMember(dest => dest.Inventories, opt => opt.Ignore())
                .ForMember(dest => dest.ProductId, opt => opt.Ignore())
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description));

            CreateMap<UpdateProductDto, Product>()
                .ForMember(dest => dest.ProductId, opt => opt.MapFrom(src => src.ProductId))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
                .ForMember(dest => dest.Price, opt => opt.MapFrom(src => src.Price))
                .ForMember(dest => dest.SubcategoryId, opt => opt.MapFrom(src => src.SubcategoryId))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.IsActive, opt => opt.Ignore());
        }
    }
}
