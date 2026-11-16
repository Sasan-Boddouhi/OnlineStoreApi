using Microsoft.AspNetCore.Http;

namespace Application.Exceptions;

/// <summary>
/// Batch 6 — ورودی از نظر قالب درست است اما از نظر قواعد کسب‌وکار پذیرفتنی نیست
/// (مثلاً نبود روش ارسال در checkout). میان‌افزار موجود آن را با کد و پیام به 422 تبدیل می‌کند.
/// </summary>
public class UnprocessableEntityException : AppException
{
    public UnprocessableEntityException(string message, string code = "UNPROCESSABLE_ENTITY")
        : base(message, code, StatusCodes.Status422UnprocessableEntity)
    {
    }
}
