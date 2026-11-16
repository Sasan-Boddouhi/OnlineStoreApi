namespace Application.Exceptions;

public abstract class AppException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    protected AppException(string message, string code, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Batch 53: سازنده‌ای که کد و وضعیت را از رجیستری <see cref="ErrorCodes"/> می‌گیرد.</summary>
    protected AppException(ErrorCodeInfo info, string? message = null)
        : this(message ?? info.Message, info.Code, info.Status)
    {
    }
}