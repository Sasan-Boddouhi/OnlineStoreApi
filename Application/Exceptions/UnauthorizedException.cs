using Microsoft.AspNetCore.Http;

namespace Application.Exceptions;

public class UnauthorizedException : AppException
{
    public UnauthorizedException(string message, string code = "UNAUTHORIZED")
        : base(message, code, StatusCodes.Status401Unauthorized)
    {
    }
}