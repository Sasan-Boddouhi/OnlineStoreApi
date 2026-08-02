using Microsoft.AspNetCore.Http;

namespace Application.Exceptions;

public class ConflictException : AppException
{
    public ConflictException(string message, string code = "CONFLICT")
        : base(message, code, StatusCodes.Status409Conflict)
    {
    }
}