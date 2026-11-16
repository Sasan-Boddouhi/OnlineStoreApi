using Microsoft.AspNetCore.Http;

namespace Application.Exceptions;

/// <summary>
/// Batch 5 (F-07): نتیجه هنوز معلوم نیست و درخواست باید بعداً تکرار شود (503 + هدر Retry-After).
/// مثلاً تأیید پرداخت وقتی درگاه پاسخ قطعی نداده است.
/// </summary>
public class ServiceUnavailableException : AppException
{
    public int RetryAfterSeconds { get; }

    public ServiceUnavailableException(string message, string code, int retryAfterSeconds)
        : base(message, code, StatusCodes.Status503ServiceUnavailable)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}
