using FluentValidation;
using BusinessLogic.DTOs.Product;

namespace BusinessLogic.Validators.Products;

public class UpdateProductDtoValidator : AbstractValidator<UpdateProductDto>
{
    public UpdateProductDtoValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("شناسه محصول معتبر نیست");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("نام محصول الزامی است")
            .MaximumLength(100).WithMessage("نام محصول حداکثر 100 کاراکتر می‌تواند باشد");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("قیمت محصول باید بزرگتر از صفر باشد");

        RuleFor(x => x.SubcategoryId)
            .GreaterThan(0).WithMessage("شناسه زیردسته‌بندی نامعتبر است");

        // Batch 49b: «پاک کردن برند» و «تعیین برند» در یک درخواست با هم تناقض دارند.
        RuleFor(x => x.ClearBrand)
            .Must((dto, clearBrand) => !clearBrand || dto.BrandId is null)
            .WithMessage(ClearBrandWithBrandIdMessage);
    }

    /// <summary>پیام 422 وقتی clearBrand همراه با brandId ارسال شود (اعتبارسنج و سرویس).</summary>
    public const string ClearBrandWithBrandIdMessage = "پاک کردن برند (clearBrand) همراه با تعیین برند (brandId) مجاز نیست.";
}