using Application.Common.Helpers;
using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Batch 45c: قاعدهٔ <see cref="OrderQuantity"/> برای یک پیشنهاد فروش، وقتی واحد کالا در دست نیست
/// (سبد، ادغام سبد مهمان، checkout، رزرو و آزادسازی موجودی).
/// واحد فقط برای مقدار کسری خوانده می‌شود: مقدار صحیح مثبت برای همهٔ واحدها مجاز است و مقدار صفر/منفی یا
/// با بیش از دو رقم اعشار برای هیچ واحدی مجاز نیست. پیشنهاد فروش ناموجود سخت‌گیرانه‌ترین واحد (عددی) را می‌گیرد.
/// </summary>
internal static class ListingQuantityRule
{
    public static async Task EnsureValidAsync(
        IUnitOfWork unitOfWork,
        int listingId,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var violation = OrderQuantity.UnitIndependentViolation(quantity);
        if (violation is not null)
            throw OrderQuantity.Invalid(violation);

        if (OrderQuantity.IsWhole(quantity))
            return;

        var row = await unitOfWork.Repository<SellerProduct>().FirstOrDefaultAsync(
            new Spec<SellerProduct>().Where(l => l.ListingId == listingId),
            l => new ListingUnit { Unit = l.Variant.Product.Unit },
            cancellationToken);

        OrderQuantity.EnsureValid(row?.Unit ?? UnitOfMeasurement.Piece, quantity);
    }

    internal sealed class ListingUnit
    {
        public UnitOfMeasurement Unit { get; init; }
    }
}
