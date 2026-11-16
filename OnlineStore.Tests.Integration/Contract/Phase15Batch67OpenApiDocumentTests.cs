using System.Text.Json;
using Application.Entities;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Contract;

/// <summary>
/// Batch 67 (API-05) — سند OpenAPI زنده: هر عملیات شناسه، schema موفقیت و پاسخ‌های خطا دارد،
/// هیچ schema‌ای نام یک entity را نمی‌گیرد، و هدرهای سبد و idempotency مستند شده‌اند.
/// </summary>
public class Phase15Batch67OpenApiDocumentTests : ControllerIntegrationTestBase
{
    private static readonly string[] HttpMethods = { "get", "put", "post", "delete", "options", "head", "patch" };

    public Phase15Batch67OpenApiDocumentTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<JsonDocument> LoadAsync()
    {
        var response = await Client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static IEnumerable<(string Path, string Method, JsonElement Op)> Operations(JsonDocument doc) =>
        doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject()
                .Where(m => HttpMethods.Contains(m.Name))
                .Select(m => (p.Name, m.Name, m.Value)));

    [Fact]
    public async Task EveryOperation_HasUniqueIdInControllerActionForm()
    {
        using var doc = await LoadAsync();
        var ids = Operations(doc).Select(o => o.Op.TryGetProperty("operationId", out var id) ? id.GetString() : null).ToList();

        ids.Should().OnlyContain(id => !string.IsNullOrEmpty(id) && id.Contains('_'));
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task EveryOperation_HasSuccessResponseAndErrorResponsesWithProblemSchema()
    {
        using var doc = await LoadAsync();

        foreach (var (path, method, op) in Operations(doc))
        {
            var responses = op.GetProperty("responses").EnumerateObject().Select(r => r.Name).ToList();
            responses.Should().Contain(s => s.StartsWith('2'), $"{method} {path} needs a success response");
            responses.Should().Contain(new[] { "400", "404", "409", "422", "429", "500" }, $"{method} {path}");

            var body = op.GetProperty("responses").GetProperty("500").GetProperty("content")
                .GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString();
            body.Should().EndWith("/ProblemDetails");
        }
    }

    [Fact]
    public async Task ProblemDetailsSchema_ListsCodeAndTraceId()
    {
        using var doc = await LoadAsync();
        var schema = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ProblemDetails");

        schema.GetProperty("properties").TryGetProperty("code", out _).Should().BeTrue();
        schema.GetProperty("properties").TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Schemas_AreNotNamedAfterEntitiesOrAnonymousTypes()
    {
        using var doc = await LoadAsync();
        var entityNames = typeof(Brand).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(Brand).Namespace && t.IsClass)
            .Select(t => t.Name).ToHashSet();

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(s => s.Name).ToList();

        schemas.Should().NotContain(s => entityNames.Contains(s));
        schemas.Should().NotContain(s => s.Contains("AnonymousType") || s.Contains('<'));
    }

    [Fact]
    public async Task CartAndCheckoutOperations_DeclareCartSessionHeader_AndInitiateDeclaresIdempotencyKey()
    {
        using var doc = await LoadAsync();

        static bool HasHeader(JsonElement op, string name) =>
            op.TryGetProperty("parameters", out var ps) &&
            ps.EnumerateArray().Any(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == name);

        var cartOps = Operations(doc).Where(o => o.Path.Contains("/carts", StringComparison.OrdinalIgnoreCase)).ToList();
        cartOps.Should().NotBeEmpty();
        cartOps.All(o => HasHeader(o.Op, "X-Cart-Session")).Should().BeTrue();
        cartOps.Should().Contain(o => o.Path.EndsWith("/checkout"));

        var initiate = Operations(doc).Single(o => o.Path.EndsWith("/payments/initiate", StringComparison.OrdinalIgnoreCase));
        HasHeader(initiate.Op, "Idempotency-Key").Should().BeTrue();

        Operations(doc).All(o => HasHeader(o.Op, "X-Client-Platform") && HasHeader(o.Op, "X-App-Version")).Should().BeTrue();
    }

    [Fact]
    public async Task NonNullableMembers_AreMarkedRequired()
    {
        using var doc = await LoadAsync();
        var order = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("OrderDto");

        order.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Contain("orderId");
    }
}
