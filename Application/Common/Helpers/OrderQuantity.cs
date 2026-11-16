using Application.Entities;
using Application.Exceptions;

namespace Application.Common.Helpers;

/// <summary>
/// Batch 45c: تنها قاعدهٔ مقدار مجاز یک قلم (سبد، سفارش، رزرو و آزادسازی موجودی).
/// <list type="bullet">
/// <item>کالای عددی (<see cref="UnitOfMeasurement.Piece"/>): فقط عدد صحیح ۱ یا بیشتر؛ نیم گوشی با نصف قیمت ممکن نیست.</item>
/// <item>کالای وزنی، حجمی یا طولی (کیلوگرم، گرم، لیتر، متر، …): بزرگ‌تر از صفر با حداکثر <see cref="MaxDecimalPlaces"/> رقم اعشار
/// (ستون‌های مقدار decimal(18,2) هستند). مقدار با رقم اعشار بیشتر رد می‌شود و هرگز بی‌صدا گرد نمی‌شود.</item>
/// </list>
/// جمع قلم همچنان با <see cref="RialMoney.LineTotal"/> به ریال کامل گرد می‌شود (Batch 45b)؛ این قاعده فقط خود مقدار را می‌سنجد.
/// خطا: 422 <c>QUANTITY_INVALID_FOR_UNIT</c> (<see cref="ErrorCodes.QuantityInvalidForUnit"/>).
/// </summary>
public static class OrderQuantity
{
    /// <summary>بیشترین رقم اعشار مقدار کالای غیرعددی؛ برابر مقیاس ستون‌های مقدار.</summary>
    public const int MaxDecimalPlaces = 2;

    public const string NotPositiveMessage = "مقدار باید بزرگ‌تر از صفر باشد.";
    public const string TooManyDecimalsMessage = "مقدار حداکثر دو رقم اعشار می‌تواند داشته باشد.";
    public const string CountUnitMessage = "این کالا عددی است؛ مقدار باید عدد صحیح ۱ یا بیشتر باشد.";

    /// <summary>واحد شمارشی (عددی) است؟ فقط «عدد»؛ بقیهٔ واحدها وزنی، حجمی یا طولی‌اند.</summary>
    public static bool IsCountUnit(UnitOfMeasurement unit) => unit == UnitOfMeasurement.Piece;

    public static bool IsWhole(decimal quantity) => quantity == decimal.Truncate(quantity);

    /// <summary>حداکثر <see cref="MaxDecimalPlaces"/> رقم اعشار؟ (صفرهای انتهایی مثل 1.250 مشکلی ندارند.)</summary>
    public static bool HasAllowedPrecision(decimal quantity) => decimal.Round(quantity, MaxDecimalPlaces) == quantity;

    /// <summary>
    /// ایرادی که به واحد بستگی ندارد (صفر/منفی، یا بیش از دو رقم اعشار)؛ null یعنی ایرادی نیست.
    /// مقدار صحیح مثبت برای همهٔ واحدها مجاز است، پس فقط مقدار کسری به دانستن واحد نیاز دارد.
    /// </summary>
    public static string? UnitIndependentViolation(decimal quantity)
    {
        if (quantity <= 0)
            return NotPositiveMessage;

        if (!HasAllowedPrecision(quantity))
            return TooManyDecimalsMessage;

        return null;
    }

    /// <summary>ایراد مقدار برای این واحد، یا null اگر مجاز است.</summary>
    public static string? Violation(UnitOfMeasurement unit, decimal quantity)
        => UnitIndependentViolation(quantity)
           ?? (IsCountUnit(unit) && !IsWhole(quantity) ? CountUnitMessage : null);

    public static bool IsValid(UnitOfMeasurement unit, decimal quantity) => Violation(unit, quantity) is null;

    /// <summary>422 <c>QUANTITY_INVALID_FOR_UNIT</c> اگر مقدار برای این واحد مجاز نیست.</summary>
    public static void EnsureValid(UnitOfMeasurement unit, decimal quantity)
    {
        var violation = Violation(unit, quantity);
        if (violation is not null)
            throw Invalid(violation);
    }

    public static UnprocessableEntityException Invalid(string message)
        => new(message, ErrorCodes.QuantityInvalidForUnit.Code);
}
