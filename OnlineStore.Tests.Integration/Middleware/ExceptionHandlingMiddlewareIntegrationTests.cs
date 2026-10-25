using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Online_Store_Application.Middleware;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Middleware;

public class ExceptionHandlingMiddlewareIntegrationTests : ControllerIntegrationTestBase
{
    public ExceptionHandlingMiddlewareIntegrationTests(
        IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task Health_DoesNotExposeExceptionDetails()
    {
        var response = await Client.GetAsync("/health");

        response.StatusCode.Should().BeOneOf(
            System.Net.HttpStatusCode.OK,
            System.Net.HttpStatusCode.ServiceUnavailable);

        var content = await response.Content.ReadAsStringAsync();

        content.Should().NotContain("\"exception\"");
    }

    [Fact]
    public async Task UnhandledException_ReturnsGenericProblemDetailsWithoutExceptionMessage()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("sensitive internal failure"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().Be("application/problem+json");

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(
            context.Response.Body,
            Encoding.UTF8,
            leaveOpen: true);

        var content = await reader.ReadToEndAsync();

        content.Should().Contain("Internal Server Error");
        content.Should().NotContain("sensitive internal failure");
    }
}
