namespace Application.Exceptions;

/// <summary>
/// Batch 7 (F-16) — نقض قاعدهٔ دامنه (مثلاً لغو سفارش ارسال‌شده، گذار نامعتبر وضعیت پرداخت).
///
/// عمداً از <see cref="InvalidOperationException"/> ارث می‌برد تا کدهای موجودی که
/// <c>catch (InvalidOperationException)</c> می‌کنند و تست‌های قبلی همچنان کار کنند، و عمداً از
/// <see cref="AppException"/> ارث نمی‌برد تا لایهٔ دامنه (Application) به HTTP وابسته نشود.
/// میان‌افزار آن را با کد و پیام به 409 تبدیل می‌کند؛ پیش‌تر هر کدام 500 می‌شد.
/// </summary>
public class BusinessRuleViolationException : InvalidOperationException
{
    public string Code { get; }

    public BusinessRuleViolationException(string message, string code = "BUSINESS_RULE_VIOLATION")
        : base(message)
    {
        Code = code;
    }
}
