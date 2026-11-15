using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Application.Entities;
using Application.Options;
using BusinessLogic.DTOs.Auth;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Contract;

/// <summary>
/// Batch 53 (API-04/12) — هر پاسخ غیر 2xx از pipeline (JWT، مسیریابی، پرس‌وجو) بدنهٔ ProblemDetails
/// با <c>code</c> و <c>traceId</c> و <c>type</c> واقعی دارد؛ پیش از این بدنهٔ 401/403/404/405/415 خالی بود.
/// </summary>
public class Phase15Batch53ErrorBodyTests : ControllerIntegrationTestBase
{
    private const string Password = "Customer@123";

    public Phase15Batch53ErrorBodyTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task ExpiredToken_Returns401WithTokenExpired()
    {
        var token = BuildToken(DateTime.UtcNow.AddMinutes(-30), DateTime.UtcNow.AddMinutes(-20));

        var response = await GetAsync("/api/v1/users", token);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "TOKEN_EXPIRED");
    }

    [Fact]
    public async Task GarbageToken_Returns401WithTokenInvalid()
    {
        var response = await GetAsync("/api/v1/users", "not-a-jwt");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "TOKEN_INVALID");
    }

    [Fact]
    public async Task MissingToken_Returns401WithUnauthenticated()
    {
        var response = await GetAsync("/api/v1/users", null);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task RevokedSession_Returns401WithSessionRevoked()
    {
        var auth = await RegisterAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessionId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken)
                .Claims.First(c => c.Type == "SessionId").Value);
            var session = await db.UserSession.FirstAsync(s => s.Id == sessionId);
            session.Status = UserSession.SessionStatus.Revoked;
            await db.SaveChangesAsync();
        }

        var response = await GetAsync("/api/v1/users", auth.AccessToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "SESSION_REVOKED");
    }

    [Fact]
    public async Task MissingPermission_Returns403WithForbidden()
    {
        var auth = await RegisterAsync();

        var response = await GetAsync("/api/v1/users", auth.AccessToken);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task UnknownRoute_Returns404WithRouteNotFound()
    {
        var response = await Client.GetAsync("/api/v1/definitely-not-a-route");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task WrongMethod_Returns405WithMethodNotAllowed()
    {
        var token = await GetAdminTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/cities");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, "METHOD_NOT_ALLOWED");
    }

    [Fact]
    public async Task UnsupportedContentType_Returns415WithUnsupportedMediaType()
    {
        var response = await Client.PostAsync("/api/v1/auth/login",
            new StringContent("phone=1", Encoding.UTF8, "text/plain"));

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE");
    }

    [Theory]
    [InlineData("/api/v1/cities?filter=Name%20eq")]
    [InlineData("/api/v1/provinces?filter=Name%20eq")]
    [InlineData("/api/v1/subcategories?filter=Name%20eq")]
    [InlineData("/api/v1/products?filter=Name%20eq")]
    public async Task BadFilter_Returns400WithInvalidQuery(string url)
    {
        var response = await Client.GetAsync(url);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "INVALID_QUERY");
    }

    [Fact]
    public async Task InvalidInput_Returns422WithValidationErrorCodeAndPersianDetail()
    {
        // FluentValidation → ExceptionHandlingMiddleware.
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            PhoneNumber = "123",
            Password,
            FirstName = "Batch",
            LastName = "Fiftythree",
            DeviceId = "b53-device"
        });

        var body = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        body.GetProperty("detail").GetString().Should().MatchRegex(@"[؀-ۿ]");
    }

    [Fact]
    public async Task MalformedJson_Returns422WithValidationErrorCodeAndPersianDetail()
    {
        // Model binding → ApiBehaviorOptions.InvalidModelStateResponseFactory.
        var response = await Client.PostAsync("/api/v1/auth/login",
            new StringContent("{\"phoneNumber\":", Encoding.UTF8, "application/json"));

        var body = await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        body.GetProperty("detail").GetString().Should().MatchRegex(@"[؀-ۿ]");
    }

    [Fact]
    public async Task UnhandledException_Returns500WithInternalErrorCodeAndPersianDetail()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new Online_Store_Application.Middleware.ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Online_Store_Application.Middleware.ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = JsonDocument.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync()).RootElement;
        body.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        body.GetProperty("type").GetString().Should().StartWith("urn:onlinestore:error:");
        body.GetProperty("detail").GetString().Should().NotContain("boom").And.MatchRegex(@"[\u0600-\u06FF]");
    }

    // ==================== helpers ====================

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, raw);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json", raw);

        var body = JsonDocument.Parse(raw).RootElement.Clone();
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("status").GetInt32().Should().Be((int)status);
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("type").GetString().Should().NotContain("yourstore").And.StartWith("urn:onlinestore:error:");
        return body;
    }

    private Task<HttpResponseMessage> GetAsync(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.SendAsync(request);
    }

    private async Task<AuthResultDto> RegisterAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            PhoneNumber = $"0914{Random.Shared.Next(1000000, 9999999):D7}",
            Password,
            FirstName = "Batch",
            LastName = "Fiftythree",
            DeviceId = "b53-device"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
    }

    private string BuildToken(DateTime notBefore, DateTime expires)
    {
        var jwt = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            jwt.Issuer, jwt.Audience,
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim("SessionId", Guid.NewGuid().ToString())
            },
            notBefore, expires, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
