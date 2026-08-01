using System.Net;
using System.Text.Json;
using Application.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;


    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }


    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }

        catch (BusinessException ex)
        {
            _logger.LogWarning(
                ex,
                "Business exception occurred");

            await WriteResponse(
                context,
                HttpStatusCode.BadRequest,
                "خطای کسب و کار",
                ex.Message);
        }


        catch (ValidationException ex)
        {
            _logger.LogWarning(
                ex,
                "Validation exception occurred");


            context.Response.StatusCode =
                (int)HttpStatusCode.UnprocessableEntity;


            context.Response.ContentType =
                "application/problem+json";


            var errors =
                ex.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(e => e.ErrorMessage)
                          .ToArray());


            var response =
                new ValidationProblemDetails(errors)
                {
                    Status = 422,
                    Title = "Validation Error"
                };


            await context.Response.WriteAsync(
                JsonSerializer.Serialize(response));
        }


        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception");


            await WriteResponse(
                context,
                HttpStatusCode.InternalServerError,
                "خطای سرور",
                "خطایی رخ داده است.");
        }
    }



    private static async Task WriteResponse(
        HttpContext context,
        HttpStatusCode status,
        string title,
        string detail)
    {

        context.Response.StatusCode = (int)status;

        context.Response.ContentType =
            "application/problem+json";


        var problem = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Detail = detail
        };


        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem));
    }
}