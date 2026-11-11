using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using Application.Options;
using BusinessLogic.DTOs.Media;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Online_Store_Application.Controllers;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 48c (SEC-12): آدرس تصویر خارجی فقط با https، بدون user-info، با پورت پیش‌فرض و میزبان مجاز پذیرفته می‌شود.
/// آپلود محلی مشمول فهرست نیست؛ حذف تصویر خارجی هرگز فایل محلی را پاک نمی‌کند.
/// </summary>
public class Phase15Batch48cImageHostTests : ControllerIntegrationTestBase
{
    public Phase15Batch48cImageHostTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<int> CreateProductAsync()
    {
        var db = GetService<AppDbContext>();
        var product = new Product
        {
            Name = "B48c-" + Guid.NewGuid().ToString("N")[..8],
            Price = 100,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece
        };
        db.Product.Add(product);
        await db.SaveChangesAsync();
        return product.ProductId;
    }

    private async Task AuthenticateAsync(HttpClient client)
    {
        var token = await GetAdminTokenAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    private static Task<HttpResponseMessage> AddByUrlAsync(HttpClient client, int productId, string url)
        => client.PostAsJsonAsync(
            $"/api/v1/products/{productId}/images/url",
            new AddProductImageUrlRequest { Url = url, Alt = "x" });

    private WebApplicationFactoryHandle EmptyAllowListFactory()
        => new(Factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.PostConfigure<ProductImageUrlOptions>(o => o.AllowedExternalHosts = []))));

    private sealed class WebApplicationFactoryHandle : IDisposable
    {
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;
        public HttpClient Client { get; }

        public WebApplicationFactoryHandle(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            Client = factory.CreateClient();
        }

        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
        }
    }

    [Theory]
    [InlineData("https://cdn.example.test/a.png")]
    [InlineData("https://CDN.Example.TEST/a.png")]
    [InlineData("https://images.example.test/deep/path/a.png?v=2")]
    [InlineData("  https://cdn.example.test/a.png  ")]
    public async Task AddByUrl_AllowedHost_Returns201(string url)
    {
        var productId = await CreateProductAsync();
        await AuthenticateAsync(Client);

        var response = await AddByUrlAsync(Client, productId, url);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("https://evil.example/a.png")]
    [InlineData("https://cdn.example.test.evil.example/a.png")]
    [InlineData("https://sub.cdn.example.test/a.png")]
    [InlineData("https://cdn.example.test@evil.example/x.png")]
    [InlineData("https://user:pw@cdn.example.test/x.png")]
    [InlineData("https://cdn.example.test:8443/a.png")]
    public async Task AddByUrl_HostNotAllowedOrTricky_Returns422ImageHostNotAllowed(string url)
    {
        var productId = await CreateProductAsync();
        await AuthenticateAsync(Client);

        var response = await AddByUrlAsync(Client, productId, url);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("IMAGE_HOST_NOT_ALLOWED");
    }

    [Fact]
    public async Task AddByUrl_HttpScheme_StillReturns422ValidationError()
    {
        var productId = await CreateProductAsync();
        await AuthenticateAsync(Client);

        var response = await AddByUrlAsync(Client, productId, "http://cdn.example.test/a.png");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(response)).Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task AddByUrl_EmptyAllowList_RefusesEveryExternalUrl()
    {
        var productId = await CreateProductAsync();
        using var handle = EmptyAllowListFactory();
        await AuthenticateAsync(handle.Client);

        foreach (var url in new[] { "https://cdn.example.test/a.png", "https://images.example.test/a.png" })
        {
            var response = await AddByUrlAsync(handle.Client, productId, url);

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await CodeAsync(response)).Should().Be("IMAGE_HOST_NOT_ALLOWED");
        }
    }

    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3, 4];

    private static async Task<ProductImageDto> UploadPngAsync(HttpClient client, int productId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "photo.png");

        var response = await client.PostAsync($"/api/v1/products/{productId}/images", form);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductImageDto>())!;
    }

    private static string DiskPath(string localUrl)
        => Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", Path.GetFileName(localUrl));

    [Fact]
    public async Task Upload_LocalUrl_IsNotSubjectToHostList()
    {
        var productId = await CreateProductAsync();
        await AuthenticateAsync(Client);
        using var handle = EmptyAllowListFactory();
        await AuthenticateAsync(handle.Client);

        var withList = await UploadPngAsync(Client, productId);
        var emptyList = await UploadPngAsync(handle.Client, productId);

        try
        {
            withList.Url.Should().StartWith("/uploads/");
            emptyList.Url.Should().StartWith("/uploads/");
            File.Exists(DiskPath(withList.Url)).Should().BeTrue();
        }
        finally
        {
            File.Delete(DiskPath(withList.Url));
            File.Delete(DiskPath(emptyList.Url));
        }
    }

    [Fact]
    public async Task Delete_ExternalUrlWithSameFileNameAsUpload_KeepsTheUploadedFile()
    {
        var productId = await CreateProductAsync();
        await AuthenticateAsync(Client);
        var uploaded = await UploadPngAsync(Client, productId);
        var diskPath = DiskPath(uploaded.Url);
        try
        {
            var fileName = Path.GetFileName(uploaded.Url);
            var add = await AddByUrlAsync(Client, productId, $"https://cdn.example.test/other/{fileName}");
            add.StatusCode.Should().Be(HttpStatusCode.Created);
            var external = (await add.Content.ReadFromJsonAsync<ProductImageDto>())!;

            var delete = await Client.DeleteAsync($"/api/v1/products/{productId}/images/{external.ProductImageId}");

            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(diskPath).Should().BeTrue("a local file is only removed for a local /uploads URL");
        }
        finally
        {
            if (File.Exists(diskPath)) File.Delete(diskPath);
        }
    }

    [Fact]
    public async Task Delete_UploadedImage_RemovesTheLocalFile()
    {
        var productId = await CreateProductAsync();
        await AuthenticateAsync(Client);
        var uploaded = await UploadPngAsync(Client, productId);
        var diskPath = DiskPath(uploaded.Url);

        var delete = await Client.DeleteAsync($"/api/v1/products/{productId}/images/{uploaded.ProductImageId}");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        File.Exists(diskPath).Should().BeFalse();
    }
}
