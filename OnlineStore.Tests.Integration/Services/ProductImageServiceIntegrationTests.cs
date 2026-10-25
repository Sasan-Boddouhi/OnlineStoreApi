using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.Services.Implementations;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 5 — گالری تصاویر محصول.
/// ذخیره‌سازی با Fake جایگزین می‌شود تا تست به دیسک وابسته نباشد.
/// </summary>
public class ProductImageServiceIntegrationTests : BaseIntegrationTest
{
    private readonly FakeStorageService _storage = new();

    private AppDbContext DbContext => GetService<AppDbContext>();

    public ProductImageServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private ProductImageService CreateService()
        => new(
            GetService<IUnitOfWork>(),
            _storage,
            Options.Create(new StorageSettings()),
            NullLogger<ProductImageService>.Instance);

    private async Task<Product> CreateProductAsync()
    {
        var product = new Product { Name = $"Img-{Guid.NewGuid():N}", Price = 10, SubcategoryId = 1 };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();
        return product;
    }

    private sealed class FakeStorageService : IStorageService
    {
        public List<string> Saved { get; } = new();
        public List<string> Deleted { get; } = new();

        public Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
        {
            var url = $"https://cdn.test/{Guid.NewGuid():N}.png";
            Saved.Add(url);
            return Task.FromResult(url);
        }

        public Task DeleteAsync(string url, CancellationToken cancellationToken = default)
        {
            Deleted.Add(url);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FirstImage_BecomesPrimaryAutomatically()
    {
        var service = CreateService();
        var product = await CreateProductAsync();

        var image = await service.AddAsync(product.ProductId, "https://cdn.test/a.png", "a", isPrimary: false);

        image.IsPrimary.Should().BeTrue();
        image.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task AddingNewPrimary_ClearsPreviousPrimary()
    {
        var service = CreateService();
        var product = await CreateProductAsync();

        var first = await service.AddAsync(product.ProductId, "https://cdn.test/a.png", null, false);
        var second = await service.AddAsync(product.ProductId, "https://cdn.test/b.png", null, true);

        var images = (await service.GetForProductAsync(product.ProductId)).ToList();

        images.Should().HaveCount(2);
        images.Single(i => i.ProductImageId == first.ProductImageId).IsPrimary.Should().BeFalse();
        images.Single(i => i.ProductImageId == second.ProductImageId).IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task DeletingPrimaryImage_PromotesNextImage()
    {
        var service = CreateService();
        var product = await CreateProductAsync();

        var first = await service.AddAsync(product.ProductId, "https://cdn.test/a.png", null, false);
        var second = await service.AddAsync(product.ProductId, "https://cdn.test/b.png", null, false);

        await service.DeleteAsync(product.ProductId, first.ProductImageId);

        var images = (await service.GetForProductAsync(product.ProductId)).ToList();

        images.Should().HaveCount(1);
        images[0].ProductImageId.Should().Be(second.ProductImageId);
        images[0].IsPrimary.Should().BeTrue();

        _storage.Deleted.Should().Contain("https://cdn.test/a.png");
    }

    [Fact]
    public async Task Delete_UnknownImage_ReturnsNotFound()
    {
        var service = CreateService();
        var product = await CreateProductAsync();

        var act = () => service.DeleteAsync(product.ProductId, 999_999);

        var ex = await act.Should().ThrowAsync<NotFoundException>();
        ex.And.Code.Should().Be("IMAGE_NOT_FOUND");
    }

    [Fact]
    public async Task Add_ForUnknownProduct_ReturnsNotFound()
    {
        var service = CreateService();

        var act = () => service.AddAsync(999_999, "https://cdn.test/a.png", null, false);

        var ex = await act.Should().ThrowAsync<NotFoundException>();
        ex.And.Code.Should().Be("PRODUCT_NOT_FOUND");
    }

    [Fact]
    public async Task Add_EleventhImage_IsRejected()
    {
        var service = CreateService();
        var product = await CreateProductAsync();

        for (var i = 0; i < 10; i++)
            await service.AddAsync(product.ProductId, $"https://cdn.test/{i}.png", null, false);

        var act = () => service.AddAsync(product.ProductId, "https://cdn.test/11.png", null, false);

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("IMAGE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Upload_SavesFileThroughStorage_AndRegistersUrl()
    {
        var service = CreateService();
        var product = await CreateProductAsync();

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var image = await service.UploadAsync(
            product.ProductId, stream, "photo.png", "image/png", "alt text", isPrimary: false);

        image.Url.Should().StartWith("https://cdn.test/");
        _storage.Saved.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetForProduct_ReturnsImagesOrderedBySortOrder()
    {
        var service = CreateService();
        var product = await CreateProductAsync();

        var a = await service.AddAsync(product.ProductId, "https://cdn.test/a.png", null, false);
        var b = await service.AddAsync(product.ProductId, "https://cdn.test/b.png", null, false);
        var c = await service.AddAsync(product.ProductId, "https://cdn.test/c.png", null, false);

        var images = (await service.GetForProductAsync(product.ProductId)).ToList();

        images.Select(i => i.ProductImageId)
            .Should().ContainInOrder(a.ProductImageId, b.ProductImageId, c.ProductImageId);
    }
}