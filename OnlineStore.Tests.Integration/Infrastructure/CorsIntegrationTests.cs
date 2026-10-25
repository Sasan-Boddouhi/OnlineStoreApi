using System.Net;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// Phase 5 — سبد مهمان به هدر سفارشی X-Cart-Session وابسته است.
/// اگر CORS آن را در preflight رد کند، سبد مهمان در مرورگر کار نمی‌کند،
/// بدون اینکه تست‌های سرویس چیزی نشان دهند.
/// </summary>
public class CorsIntegrationTests : BaseIntegrationTest
{
    private const string AllowedOrigin = "http://localhost:5173";

    public CorsIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task Preflight_AllowsCartSessionHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/carts/current");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "X-Cart-Session");

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);

        response.Headers.TryGetValues("Access-Control-Allow-Headers", out var allowed).Should().BeTrue(
            "پیش‌پرواز باید هدرهای مجاز را برگرداند");

        var headerValue = string.Join(",", allowed!);

        // AllowAnyHeader یا ستاره برمی‌گرداند یا همان هدرهای درخواستی را بازمی‌تاباند؛
        // هر دو یعنی X-Cart-Session مجاز است.
        var headerAllowed = headerValue == "*"
            || headerValue.Contains("X-Cart-Session", StringComparison.OrdinalIgnoreCase);

        headerAllowed.Should().BeTrue($"X-Cart-Session should be allowed, but got:{headerValue}");
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_IsNotAllowed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/carts/current");
        request.Headers.Add("Origin", "http://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await Client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}