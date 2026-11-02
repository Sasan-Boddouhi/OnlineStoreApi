using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using Application.Interfaces.Services;
using AutoMapper;
using BusinessLogic.DTOs.City;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.ProductCategory;
using BusinessLogic.DTOs.Province;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace OnlineStore.Tests.Unit.Services;

public class CachingIntegrationTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly Mock<ICurrentUserService> _userMock = new();
    private readonly Mock<IAuditService> _auditMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();

    [Fact]
    public async Task ProductService_GetByIdAsync_UsesCacheOnHit()
    {
        var expected = new ProductDto
        {
            ProductId = 42,
            Name = "Cached Product",
            SubcategoryId = 1,
            SubcategoryName = "Sub1",
            CategoryId = 2,
            CategoryName = "Cat2"
        };

        _cacheMock.Setup(c => c.GetAsync<ProductDto>("products:42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var service = new ProductService(
            _uowMock.Object, _mapperMock.Object, _userMock.Object, _auditMock.Object,
            Mock.Of<ILogger<ProductService>>(), _cacheMock.Object);

        var result = await service.GetByIdAsync(42);

        result.Should().BeEquivalentTo(expected);
        _uowMock.Verify(u => u.Repository<Product>(), Times.Never);
    }

    [Fact]
    public async Task ProductCategoryService_GetAllAsync_UsesCacheOnHit()
    {
        var expected = new List<ProductCategoryDto> { new ProductCategoryDto() };
        _cacheMock.Setup(c => c.GetAsync<List<ProductCategoryDto>>("categories:all", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var service = new ProductCategoryService(
            _uowMock.Object, _mapperMock.Object, _userMock.Object,
            Mock.Of<ILogger<ProductCategoryService>>(), _cacheMock.Object);

        var result = await service.GetAllAsync();

        result.Should().BeEquivalentTo(expected);
        _uowMock.Verify(u => u.Repository<ProductCategory>(), Times.Never);
    }

    [Fact]
    public async Task ProvinceService_GetByIdAsync_UsesCacheOnHit()
    {
        var expected = new ProvinceDto { ProvinceId = 5, ProvinceName = "Tehran" };
        _cacheMock.Setup(c => c.GetAsync<ProvinceDto>("provinces:5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var service = new ProvinceService(
            _uowMock.Object, _mapperMock.Object,
            Mock.Of<ILogger<ProvinceService>>(), _cacheMock.Object);

        var result = await service.GetByIdAsync(5);

        result.Should().BeEquivalentTo(expected);
        _uowMock.Verify(u => u.Repository<Province>(), Times.Never);
    }

    [Fact]
    public async Task CityService_GetByIdAsync_UsesCacheOnHit()
    {
        var expected = new CityDto { CityId = 12, CityName = "Tehran", ProvinceId = 5 };
        _cacheMock.Setup(c => c.GetAsync<CityDto>("cities:12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var service = new CityService(
            _uowMock.Object, _mapperMock.Object,
            Mock.Of<ILogger<CityService>>(), _cacheMock.Object);

        var result = await service.GetByIdAsync(12);

        result.Should().BeEquivalentTo(expected);
        _uowMock.Verify(u => u.Repository<City>(), Times.Never);
    }

    [Fact]
    public void QueryContract_ToCacheKey_IsDeterministic()
    {
        var query1 = new QueryContract<Product>
        {
            Page = 1,
            Size = 20
        };

        var query2 = new QueryContract<Product>
        {
            Page = 1,
            Size = 20
        };

        var key1 = query1.ToCacheKey("products");
        var key2 = query2.ToCacheKey("products");

        key1.Should().Be(key2);
        key1.Should().StartWith("products:all:p1:s20");
    }

    [Fact]
    public async Task ProductService_GetByQueryAsync_UsesDeterministicCacheKey()
    {
        var query = new QueryContract<Product>
        {
            Page = 1,
            Size = 10
        };

        var expectedResult = new PagedResult<ProductDto>
        {
            Items = new List<ProductDto>
            {
                new ProductDto
                {
                    ProductId = 1,
                    Name = "Cached P1",
                    SubcategoryId = 1,
                    SubcategoryName = "Sub1",
                    CategoryId = 1,
                    CategoryName = "Cat1"
                }
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };

        _cacheMock.Setup(c => c.GetAsync<string>("products:version", It.IsAny<CancellationToken>()))
            .ReturnsAsync("v1");

        var expectedKey = $"{query.ToCacheKey("products")}:v1";
        _cacheMock.Setup(c => c.GetAsync<PagedResult<ProductDto>>(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var service = new ProductService(
            _uowMock.Object, _mapperMock.Object, _userMock.Object, _auditMock.Object,
            Mock.Of<ILogger<ProductService>>(), _cacheMock.Object);

        var result = await service.GetByQueryAsync(query);

        result.Should().BeEquivalentTo(expectedResult);
        _uowMock.Verify(u => u.Repository<Product>(), Times.Never);
    }
}
