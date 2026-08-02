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
}