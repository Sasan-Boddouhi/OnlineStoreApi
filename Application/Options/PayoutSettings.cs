namespace Application.Options;

/// <summary>
/// Batch 44 (FIN-02) — تنظیمات تسویهٔ فروشنده.
/// <para>
/// تصمیم مالک: پنجرهٔ مرجوعی ۷ روز پس از تحویل است و تسویه فقط پس از بسته شدن آن قابل پرداخت است.
/// تسویهٔ هر بسته هنگام تحویل ساخته می‌شود با <c>AvailableAtUtc = DeliveredAt + HoldDays</c>؛
/// پرداخت پیش از آن 409 <c>PAYOUT_ON_HOLD</c> است. مرجوعی در این بازه تسویهٔ Pending را لغو می‌کند.
/// </para>
/// </summary>
public sealed class PayoutSettings
{
    public const string SectionName = "PayoutSettings";

    /// <summary>کمینه و بیشینهٔ مجاز <see cref="HoldDays"/> (اعتبارسنجی هنگام راه‌اندازی).</summary>
    public const int MinHoldDays = 0;
    public const int MaxHoldDays = 90;

    /// <summary>چند روز پس از تحویل، تسویهٔ بسته قابل پرداخت می‌شود. پیش‌فرض ۷ (پنجرهٔ مرجوعی).</summary>
    public int HoldDays { get; set; } = 7;

    /// <summary>آیا مقدار <see cref="HoldDays"/> در بازهٔ مجاز است.</summary>
    public bool IsValid() => HoldDays is >= MinHoldDays and <= MaxHoldDays;
}
