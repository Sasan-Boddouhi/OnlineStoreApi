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

        // Phase 6 — واریانت‌ها و مقادیر ویژگی‌هایشان.
        CreateMap<ProductVariant, ProductVariantDto>()
            .ForMember(dest => dest.Attributes, opt => opt.MapFrom(src => src.AttributeValues));

        CreateMap<SellerProduct, VariantListingDto>()
            .ForMember(dest => dest.SellerStoreName, opt => opt.MapFrom(src => src.Seller != null ? src.Seller.StoreName : string.Empty))
            .ForMember(dest => dest.AvailableQuantity, opt => opt.MapFrom(src => src.Inventories != null ? src.Inventories.Where(inv => inv.IsActive).Sum(inv => inv.Quantity) : 0m));

        CreateMap<ProductAttributeValue, VariantAttributeDto>()
            .ForMember(dest => dest.Name,
                opt => opt.MapFrom(src => src.Attribute != null ? src.Attribute.Name : string.Empty))
            .ForMember(dest => dest.Type,
                opt => opt.MapFrom(src => src.Attribute != null ? src.Attribute.Type.ToString() : "Text"));

        // Phase 0: Unit, Barcode, ImageUrl و ExpirationDate قبلاً Ignore می‌شدند و
        // در نتیجه هنگام Create دور ریخته می‌شدند.
        CreateMap<CreateProductDto, Product>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.ProductId, opt => opt.Ignore())
            // Batch 49 (F-30): BrandId پیش‌تر Ignore می‌شد و برند هرگز ثبت نمی‌شد.
            // اعتبار برند (موجود و فعال) در ProductService بررسی می‌شود.
            .ForMember(dest => dest.BrandId, opt => opt.MapFrom(src => src.BrandId))
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true))
            // Phase 9 (R33): کش امتیاز ورودی کاربر نیست.
            .ForMember(dest => dest.AverageRating, opt => opt.Ignore())
            .ForMember(dest => dest.ReviewCount, opt => opt.Ignore());

        // Phase 0: فیلدهای nullable فقط وقتی مقدار دارند روی موجودیت اعمال می‌شوند
        // تا Update جزئی، مقدار فعلی را صفر/خالی نکند.
        CreateMap<UpdateProductDto, Product>()
            .ConfigureDbDestination()
            // Batch 49 (F-30): مانند Unit/IsActive، null یعنی «بدون تغییر»، نه «حذف برند».
            // Batch 49b: حذف برند فقط با پرچم صریح ClearBrand (ترکیب آن با BrandId پیش‌تر 422 شده است).
            .ForMember(dest => dest.BrandId, opt =>
            {
                opt.PreCondition(src => src.BrandId.HasValue || src.ClearBrand);
                opt.MapFrom(src => src.ClearBrand ? null : src.BrandId);
            })
            .ForMember(dest => dest.IsActive, opt =>
            {
                opt.PreCondition(src => src.IsActive.HasValue);
                opt.MapFrom(src => src.IsActive!.Value);
            })
            .ForMember(dest => dest.Unit, opt =>
            {
                opt.PreCondition(src => src.Unit.HasValue);
                opt.MapFrom(src => src.Unit!.Value);
            })
            .ForMember(dest => dest.AverageRating, opt => opt.Ignore())
            .ForMember(dest => dest.ReviewCount, opt => opt.Ignore());

        // M7.1: Variant Update mapping
        CreateMap<UpdateProductVariantDto, ProductVariant>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.VariantId, opt => opt.Ignore())
            .ForMember(dest => dest.ProductId, opt => opt.Ignore())
            .ForMember(dest => dest.AttributeValues, opt => opt.Ignore());

        CreateMap<CreateProductVariantDto, ProductVariant>()
            .ConfigureDbDestination()
            .ForMember(dest => dest.VariantId, opt => opt.Ignore())
            .ForMember(dest => dest.Product, opt => opt.Ignore())
            .ForMember(dest => dest.AttributeValues, opt => opt.Ignore());
    }
}
