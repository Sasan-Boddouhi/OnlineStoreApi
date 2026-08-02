using Microsoft.AspNetCore.Http;

namespace Application.Exceptions;

public class NotFoundException : AppException
{
    public NotFoundException(string message, string code = "NOT_FOUND")
        : base(message, code, StatusCodes.Status404NotFound)
    {
    }
}