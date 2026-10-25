using Microsoft.AspNetCore.Http;

namespace Application.Exceptions;

public class BusinessException : AppException
{
    public BusinessException(string message, string code = "BUSINESS_ERROR")
        : base(message, code, StatusCodes.Status400BadRequest)
    {
    }
}