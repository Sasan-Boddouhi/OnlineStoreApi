using Microsoft.AspNetCore.Http;

namespace Application.Exceptions;

public class ForbiddenException : AppException
{
    public ForbiddenException(string message, string code = "FORBIDDEN")
        : base(message, code, StatusCodes.Status403Forbidden)
    {
    }
}