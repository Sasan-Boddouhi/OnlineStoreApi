using Application.Common.Queries;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Specifications.Products;
using FluentValidation;
using ProductEntity = Application.Entities.Product;

namespace BusinessLogic.Validators.Products;

/// <summary>
/// Batch 60 (API-10): پارامترهای نوع‌دار فهرست محصولات؛ خطا از مسیر مدل به 422 VALIDATION_ERROR می‌رسد.
/// خطای دستوری <c>filter</c> اینجا بررسی نمی‌شود و در کنترلر 400 INVALID_QUERY می‌گیرد.
/// </summary>
public sealed class ProductListQueryDtoValidator : AbstractValidator<ProductListQueryDto>
{
    public const int MaxSearchLength = 100;

    private static readonly QueryParseContext<ProductEntity> SortContext = new()
    {
        AllowedFields = new HashSet<string>(ProductQueryConfig.AllowedFields),
        CaseInsensitive = true
    };

    public ProductListQueryDtoValidator()
    {
        RuleFor(x => x.Q)
            .MaximumLength(MaxSearchLength).WithMessage($"عبارت جستجو نباید بیش از {MaxSearchLength} نویسه باشد.");

        RuleFor(x => x.CategoryId)
            .GreaterThan(0).When(x => x.CategoryId.HasValue)
            .WithMessage("شناسهٔ دسته‌بندی باید عددی بزرگ‌تر از صفر باشد.");

        RuleFor(x => x.SubcategoryId)
            .GreaterThan(0).When(x => x.SubcategoryId.HasValue)
            .WithMessage("شناسهٔ زیردسته باید عددی بزرگ‌تر از صفر باشد.");

        RuleFor(x => x.BrandId)
            .GreaterThan(0).When(x => x.BrandId.HasValue)
            .WithMessage("شناسهٔ برند باید عددی بزرگ‌تر از صفر باشد.");

        RuleFor(x => x.Sort)
            .Must(BeKnownSort)
            .WithMessage("مرتب‌سازی پشتیبانی نمی‌شود؛ مقدارهای مجاز: newest، rating، name یا فیلدهای مجاز با «-» برای نزولی.");
    }

    private static bool BeKnownSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return true;

        if (ProductListCriteria.ParseSortPreset(sort) is not null)
            return true;

        // سازگاری با زبان کوچک: فهرست فیلدهای مجاز با ویرگول، «-» یا « desc» برای نزولی.
        return SortParser.TryParse(sort, SortContext).Success;
    }
}
