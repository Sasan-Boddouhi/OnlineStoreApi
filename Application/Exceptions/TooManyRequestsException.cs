namespace Application.Exceptions;

/// <summary>
/// Batch 46: 429 از لایهٔ سرویس (مثلاً سقف ارسال OTP برای یک شماره)، با هدر <c>Retry-After</c> و <c>retryAfterSeconds</c>
/// مانند پاسخ limiter (RATE_LIMIT_EXCEEDED).
/// </summary>
public class TooManyRequestsException : AppException
{
    public int RetryAfterSeconds { get; }

    public TooManyRequestsException(ErrorCodeInfo info, int retryAfterSeconds, string? message = null)
        : base(info, message)
    {
        RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
    }
}
