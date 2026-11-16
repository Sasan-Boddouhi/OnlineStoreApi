namespace Application.Interfaces;

/// <summary>Batch 46: یک پیامک خروجی. شماره همیشه شکل استاندارد <c>09xxxxxxxxx</c> است.</summary>
public sealed record SmsMessage(string PhoneNumber, string Text);

/// <summary>
/// Batch 46 (N9، review §9.3 قاعدهٔ ۶): ارسال پیامک. بیرون از Production یک فرستندهٔ لاگ‌کننده کافی است؛
/// در Production راه‌اندازی بدون فرستندهٔ واقعی (<see cref="IsRealProvider"/>) شکست می‌خورد.
/// آداپتور ارائه‌دهندهٔ واقعی در Batch 46b (پس از انتخاب مالک) اضافه می‌شود.
/// </summary>
public interface ISmsSender
{
    /// <summary>فقط آداپتوری که واقعاً پیامک تحویل می‌دهد true است.</summary>
    bool IsRealProvider { get; }

    /// <summary>پیامک را می‌فرستد؛ خطای ارائه‌دهنده به‌صورت استثنا بالا می‌آید. متن نباید جایی لاگ شود (حاوی کد است).</summary>
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}
