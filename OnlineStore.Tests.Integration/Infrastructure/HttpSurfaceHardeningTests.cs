using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// Batch 40 (SEC-05, SEC-06, SEC-16): سطح HTTP فقط JSON می‌پذیرد و برمی‌گرداند،
/// همهٔ پاسخ‌ها Content-Security-Policy سخت‌گیرانه دارند و CORS اعتبارنامه (cookie) را مجاز نمی‌کند.
/// </summary>
public class HttpSurfaceHardeningTests : BaseIntegrationTest
{
    private const string AllowedOrigin = "http://localhost:5173";

    public HttpSurfaceHardeningTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task XmlRequestBody_IsRejectedWith415()
    {
        using var content = new StringContent(
            "<LoginDto><PhoneNumber>09123456789</PhoneNumber><Password>Test@123</Password><DeviceId>x</DeviceId></LoginDto>",
            Encoding.UTF8,
            "application/xml");

        var response = await Client.PostAsync("/api/v1/auth/login", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task AcceptXml_StillReturnsJson()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products?pageNumber=1&pageSize=1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/api/v1/products?pageNumber=1&pageSize=1")]
    [InlineData("/api/v1/no-such-endpoint")]
    public async Task Responses_CarryRestrictiveContentSecurityPolicy(string path)
    {
        var response = await Client.GetAsync(path);

        response.Headers.TryGetValues("Content-Security-Policy", out var values).Should().BeTrue();
        values!.Single().Should().Be("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public async Task Cors_DoesNotAllowCredentials()
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/products");
        preflight.Headers.Add("Origin", AllowedOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        using var preflightResponse = await Client.SendAsync(preflight);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products?pageNumber=1&pageSize=1");
        request.Headers.Add("Origin", AllowedOrigin);

        using var response = await Client.SendAsync(request);

        // مبدأ مجاز همچنان پذیرفته می‌شود...
        preflightResponse.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();

        // ...ولی API کوکی ندارد، پس اعتبارنامه مجاز نیست.
        preflightResponse.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }
}
