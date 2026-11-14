using System.Text;
using System.Text.Json;
using Application.Entities;
using Application.Exceptions;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
        // Batch 52 (OPS-07): «/health» بیرون از Development فقط برای مدیر است؛
        // همان خروجی تفصیلی که مدیر می‌بیند هم نباید جزئیات استثنا داشته باشد.
        var token = await GetAdminTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

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

    // ==================== Batch 7 (F-16): 500 → 409 ====================

    [Fact]
    public async Task BusinessRuleViolation_Returns409WithCode()
    {
        var (status, body) = await RunAsync(new BusinessRuleViolationException("Cannot cancel shipped orders.", "ORDER_INVALID_STATE"));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("code").GetString().Should().Be("ORDER_INVALID_STATE");
        body.GetProperty("status").GetInt32().Should().Be(409);
    }

    [Fact]
    public async Task ConcurrencyConflict_Returns409WithCode()
    {
        var (status, body) = await RunAsync(new DbUpdateConcurrencyException("row version mismatch"));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("code").GetString().Should().Be("CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task UniqueConstraintViolation_Returns409WithCode_AndHidesDatabaseDetails()
    {
        // یک تخطی واقعی از ایندکس یکتا (شمارهٔ تلفن کاربر)، نه استثنای ساختگی.
        var db = GetService<AppDbContext>();
        var phone = $"09{Random.Shared.Next(100000000, 999999999)}";
        db.User.Add(NewUser(phone));
        await db.SaveChangesAsync();
        db.User.Add(NewUser(phone));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var (status, body) = await RunAsync(duplicate);

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("code").GetString().Should().Be("DUPLICATE_RECORD");
        body.GetProperty("detail").GetString().Should().NotContain("UNIQUE").And.NotContain("User");
    }

    [Fact]
    public async Task OtherDatabaseUpdateError_StaysA500()
    {
        var (status, _) = await RunAsync(new DbUpdateException("boom", new InvalidOperationException("disk full")));

        status.Should().Be(StatusCodes.Status500InternalServerError);
    }

    private static async Task<(int Status, JsonElement Body)> RunAsync(Exception toThrow)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw toThrow,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, doc.RootElement.Clone());
    }

    private static User NewUser(string phone) => new()
    {
        FirstName = "B7",
        LastName = "Dup",
        PhoneNumber = phone,
        PasswordHash = "hash",
        UserType = UserType.Customer,
        SecurityStamp = Guid.NewGuid().ToString()
    };
}
