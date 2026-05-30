using System.Linq.Expressions;
using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace OnlineStore.Tests.Unit.Services;

public class ProductServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<ILogger<ProductService>> _loggerMock;

    private readonly Mock<IGenericRepository<Product>> _productRepoMock;
    private readonly Mock<IGenericRepository<ProductSubcategory>> _subcategoryRepoMock;

    private readonly ProductService _productService;

    public ProductServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ProductService>>();

        _productRepoMock = new Mock<IGenericRepository<Product>>();
        _subcategoryRepoMock = new Mock<IGenericRepository<ProductSubcategory>>();

        _uowMock.Setup(u => u.Repository<Product>()).Returns(_productRepoMock.Object);
        _uowMock.Setup(u => u.Repository<ProductSubcategory>()).Returns(_subcategoryRepoMock.Object);

        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _productService = new ProductService(
            _uowMock.Object,
            _mapperMock.Object,
            _currentUserMock.Object,
            _loggerMock.Object);
    }

    // Helper for required fields
    private static Product CreateProduct(int id = 0, string name = "Test", decimal price = 10, int subcategoryId = 1, bool isActive = true)
        => new()
        {
            ProductId = id,
            Name = name,
            Price = price,
            SubcategoryId = subcategoryId,
            IsActive = isActive
        };

    private static ProductDto CreateProductDto(int id = 0, string name = "Test", decimal price = 10,
        int subcategoryId = 1, string subcategoryName = "Sub",
        int categoryId = 1, string categoryName = "Cat")
        => new()
        {
            ProductId = id,
            Name = name,
            Price = price,
            SubcategoryId = subcategoryId,
            SubcategoryName = subcategoryName,
            CategoryId = categoryId,
            CategoryName = categoryName
        };

    // ==================== CreateAsync ====================
    [Fact]
    public async Task CreateAsync_ValidDto_CreatesProduct()
    {
        var dto = new CreateProductDto { Name = "New", Price = 50, SubcategoryId = 1 };
        var product = CreateProduct(10, name: "New", price: 50);
        var productDto = CreateProductDto(10, name: "New", price: 50);

        _subcategoryRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<ProductSubcategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _productRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mapperMock.Setup(m => m.Map<Product>(dto)).Returns(product);
        _productRepoMock.Setup(r => r.AddAsync(product, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<ProductDto>(product)).Returns(productDto);

        var result = await _productService.CreateAsync(dto);

        result.Should().NotBeNull();
        result.ProductId.Should().Be(10);
        _uowMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsBusinessException()
    {
        var dto = new CreateProductDto { Name = "   ", Price = 10, SubcategoryId = 1 };
        Func<Task> act = () => _productService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*نام محصول*");
    }

    [Fact]
    public async Task CreateAsync_ZeroPrice_ThrowsBusinessException()
    {
        var dto = new CreateProductDto { Name = "Test", Price = 0, SubcategoryId = 1 };
        Func<Task> act = () => _productService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*قیمت*");
    }

    [Fact]
    public async Task CreateAsync_NegativePrice_ThrowsBusinessException()
    {
        var dto = new CreateProductDto { Name = "Test", Price = -5, SubcategoryId = 1 };
        Func<Task> act = () => _productService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*قیمت*");
    }

    [Fact]
    public async Task CreateAsync_InvalidSubcategory_ThrowsBusinessException()
    {
        var dto = new CreateProductDto { Name = "Test", Price = 10, SubcategoryId = 999 };
        _subcategoryRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<ProductSubcategory, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Func<Task> act = () => _productService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*زیردسته*");
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsBusinessException()
    {
        var dto = new CreateProductDto { Name = "Duplicate", Price = 100, SubcategoryId = 1 };
        _subcategoryRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<ProductSubcategory, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _productRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Func<Task> act = () => _productService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*نام*ثبت شده*");
    }

    [Fact]
    public async Task CreateAsync_SaveFails_ThrowsBusinessException()
    {
        var dto = new CreateProductDto { Name = "Good", Price = 10, SubcategoryId = 1 };
        var product = CreateProduct(name: "Good");
        _subcategoryRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<ProductSubcategory, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _productRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _mapperMock.Setup(m => m.Map<Product>(dto)).Returns(product);
        _productRepoMock.Setup(r => r.AddAsync(product, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("DB error"));
        Func<Task> act = () => _productService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>();
        _uowMock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ==================== UpdateAsync ====================
    [Fact]
    public async Task UpdateAsync_ValidDto_UpdatesProduct()
    {
        var productId = 5;
        var existing = CreateProduct(productId, name: "Old", price: 30);
        var updateDto = new UpdateProductDto { ProductId = productId, Name = "New", Price = 50, SubcategoryId = 1 };
        var updatedDto = CreateProductDto(productId, name: "New", price: 50);

        _productRepoMock.Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _productRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _productRepoMock.Setup(r => r.Update(existing));
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Correctly mock with Expression<Func<Product, ProductDto>>
        _productRepoMock
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Product>>(),
                It.IsAny<Expression<Func<Product, ProductDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        var result = await _productService.UpdateAsync(updateDto);
        result.Should().NotBeNull();
        result!.Name.Should().Be("New");
        _productRepoMock.Verify(r => r.Update(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNull()
    {
        _productRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((Product?)null);
        var result = await _productService.UpdateAsync(new UpdateProductDto { ProductId = 999 });
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_DuplicateName_ThrowsBusinessException()
    {
        var existing = CreateProduct(1, name: "Old");
        var updateDto = new UpdateProductDto { ProductId = 1, Name = "ExistingName", Price = 10, SubcategoryId = 1 };
        _productRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _productRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Func<Task> act = () => _productService.UpdateAsync(updateDto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*نام*ثبت شده*");
    }

    // ==================== DeleteAsync ====================
    [Fact]
    public async Task DeleteAsync_ExistingProduct_SetsInactiveAndReturnsTrue()
    {
        var product = CreateProduct(10, isActive: true);
        _productRepoMock.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _productService.DeleteAsync(10);
        result.Should().BeTrue();
        product.IsActive.Should().BeFalse();
        _productRepoMock.Verify(r => r.Update(product), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ProductNotFound_ReturnsFalse()
    {
        _productRepoMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((Product?)null);
        var result = await _productService.DeleteAsync(999);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_AlreadyInactive_ReturnsFalse()
    {
        var product = CreateProduct(10, isActive: false);
        _productRepoMock.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        var result = await _productService.DeleteAsync(10);
        result.Should().BeFalse();
    }

    // ==================== GetByIdAsync ====================
    [Fact]
    public async Task GetByIdAsync_ExistingProduct_ReturnsDto()
    {
        var productDto = CreateProductDto(1, name: "Test");
        _productRepoMock
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Product>>(),
                It.IsAny<Expression<Func<Product, ProductDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(productDto);

        var result = await _productService.GetByIdAsync(1);
        result.Should().BeEquivalentTo(productDto);
    }

    [Fact]
    public async Task GetByIdAsync_NonExisting_ReturnsNull()
    {
        _productRepoMock
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Product>>(),
                It.IsAny<Expression<Func<Product, ProductDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductDto?)null);

        var result = await _productService.GetByIdAsync(123);
        result.Should().BeNull();
    }

    // ==================== GetByQueryAsync ====================
    [Fact]
    public async Task GetByQueryAsync_ReturnsPagedResult()
    {
        var query = new QueryContract<Product> { Page = 1, Size = 2 };
        var products = new List<ProductDto>
        {
            CreateProductDto(1, name: "P1"),
            CreateProductDto(2, name: "P2")
        };
        var totalCount = 5;

        _productRepoMock
            .Setup(r => r.ListAsync(
                It.IsAny<Spec<Product>>(),
                It.IsAny<Expression<Func<Product, ProductDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _productRepoMock
            .Setup(r => r.CountAsync(It.IsAny<Spec<Product>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(totalCount);

        var result = await _productService.GetByQueryAsync(query);

        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(2);
    }
}