using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Contract;

/// <summary>
/// N14 — بدنهٔ JSON با تودرتویی بیش از MaxDepth (64) باید با 400/422 و بدنهٔ ProblemDetails استاندارد
/// (code + traceId) رد شود، نه با crash یا 500. هنگام مهاجرت به System.Text.Json باید MaxDepth صراحتاً 64 شود.
/// </summary>
public class N14JsonDepthTests : ControllerIntegrationTestBase
{
    public N14JsonDepthTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private static string Nested(int depth)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < depth; i++) sb.Append("{\"a\":");
        sb.Append('1');
        for (var i = 0; i < depth; i++) sb.Append('}');
        return sb.ToString();
    }

    [Theory]
    [InlineData(65)]
    [InlineData(5000)]
    public async Task BodyNestedDeeperThanMaxDepth_IsRejectedWithProblemBody(int depth)
    {
        var response = await Client.PostAsync("/api/v1/auth/login",
            new StringContent(Nested(depth), Encoding.UTF8, "application/json"));

        var raw = await response.Content.ReadAsStringAsync();
        new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity }.Should().Contain(response.StatusCode, raw);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json", raw);

        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("code").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task BodyNestedExactlyAtMaxDepth_IsNotRejectedByTheDepthGuard()
    {
        var response = await Client.PostAsync("/api/v1/auth/login",
            new StringContent(Nested(64), Encoding.UTF8, "application/json"));

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, raw);
    }
}
