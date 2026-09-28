using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;

namespace OnlineStore.Tests.Integration.Infrastructure;

public class ApiVersioningIntegrationTests : BaseIntegrationTest
{
    public ApiVersioningIntegrationTests(IntegrationTestFactory<Program> factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task VersionedProductsRoute_ReturnsOk_AndReportsSupportedVersion()
    {
        var response = await Client.GetAsync("/api/v1/products?pageNumber=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("api-supported-versions", out var versions).Should().BeTrue();
        versions!.Single().Should().Contain("1.0");
    }

    [Fact]
    public async Task UnversionedProductsRoute_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/products?pageNumber=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnsupportedVersion_ReturnsBadRequest()
    {
        var response = await Client.GetAsync("/api/v2/products?pageNumber=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SwaggerV1Document_ContainsVersionedApiPaths()
    {
        var response = await Client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        document.Should().NotBeNull();

        var paths = document!.RootElement.GetProperty("paths");
        paths.EnumerateObject().Should().Contain(p => p.Name.StartsWith("/api/v1/"));
    }
}
