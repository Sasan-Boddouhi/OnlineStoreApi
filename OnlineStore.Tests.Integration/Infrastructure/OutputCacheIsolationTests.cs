using System.Net;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Brand;
using BusinessLogic.DTOs.Cart;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// F-04: رفتار کش خروجی با pipeline واقعی (کش روشن).
/// سیاست پایهٔ سراسری هر GET بی‌توکن را بر اساس URL کش می‌کرد و هدر X-Cart-Session را نمی‌دید؛
/// پس مهمان دوم سبد مهمان اول را می‌گرفت.
/// </summary>
public sealed class OutputCacheIsolationTests : IClassFixture<OutputCacheEnabledFactory>, IAsyncLifetime
{
    private const string SessionHeader = "X-Cart-Session";

    private readonly OutputCacheEnabledFactory _factory;

    public OutputCacheIsolationTests(OutputCacheEnabledFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateGuestClient(string sessionId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(SessionHeader, sessionId);
        return client;
    }

    [Fact]
    public async Task GetCurrentCart_TwoGuestsWithDifferentSessions_EachGetsOwnCart()
    {
        using var guestA = CreateGuestClient(Guid.NewGuid().ToString());
        using var guestB = CreateGuestClient(Guid.NewGuid().ToString());

        var responseA = await guestA.GetAsync("/api/v1/carts/current");
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartA = await responseA.Content.ReadFromJsonAsync<CartDto>();

        var responseB = await guestB.GetAsync("/api/v1/carts/current");
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartB = await responseB.Content.ReadFromJsonAsync<CartDto>();

        cartA!.IsAnonymous.Should().BeTrue();
        cartB!.IsAnonymous.Should().BeTrue();
        cartB.CartId.Should().NotBe(cartA.CartId, "guest B must never receive guest A's cart");
        responseB.Headers.Age.Should().BeNull("a cart response must never come from the output cache");
    }

    [Fact]
    public async Task GetBrands_SecondRequest_IsServedFromOutputCache()
    {
        using var client = _factory.CreateClient();

        var first = await client.GetAsync("/api/v1/brands");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<List<BrandDto>>();

        // برند مستقیم در دیتابیس اضافه می‌شود (بدون عبور از API)، پس فقط پاسخ کش‌شده آن را نمی‌بیند.
        var newBrandName = $"Brand-{Guid.NewGuid():N}"[..20];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Brand.Add(new Brand { Name = newBrandName, Slug = newBrandName.ToLowerInvariant(), IsActive = true });
            await db.SaveChangesAsync();
        }

        var second = await client.GetAsync("/api/v1/brands");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<List<BrandDto>>();

        second.Headers.Age.Should().NotBeNull("the second response must be a cache hit");
        secondBody.Should().BeEquivalentTo(firstBody);
        secondBody!.Select(b => b.Name).Should().NotContain(newBrandName);

        // query string جزو کلید نیست، پس query تصادفی ورودی تازه نمی‌سازد.
        var withRandomQuery = await client.GetAsync($"/api/v1/brands?x={Guid.NewGuid():N}");
        withRandomQuery.Headers.Age.Should().NotBeNull("random query strings must not create new cache entries");
    }

    [Fact]
    public async Task GetCategories_CachedResponse_CarriesRequestingOriginCorsHeader()
    {
        using var client = _factory.CreateClient();

        var fromFirstOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/v1/categories");
        fromFirstOrigin.Headers.Add("Origin", "http://localhost:3000");
        var first = await client.SendAsync(fromFirstOrigin);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var fromSecondOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/v1/categories");
        fromSecondOrigin.Headers.Add("Origin", "http://localhost:5173");
        var second = await client.SendAsync(fromSecondOrigin);

        second.Headers.Age.Should().NotBeNull("the second response must be a cache hit");
        second.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("http://localhost:5173");
    }

    [Fact]
    public async Task GetProducts_List_IsNotOutputCached()
    {
        using var client = _factory.CreateClient();

        await client.GetAsync("/api/v1/products");
        var second = await client.GetAsync("/api/v1/products");

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.Age.Should().BeNull();
    }
}
