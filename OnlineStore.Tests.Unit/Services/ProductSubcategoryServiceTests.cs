using System.Linq.Expressions;
using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Application.Common.Specifications;

namespace OnlineStore.Tests.Unit.Services;

public class ProductSubcategoryServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<ILogger<ProductSubcategoryService>> _loggerMock;
    private readonly ProductSubcategoryService _service;

    private readonly Mock<IGenericRepository<ProductSubcategory>> _subcategoryRepoMock;
    private readonly Mock<IGenericRepository<ProductCategory>> _categoryRepoMock;

    public ProductSubcategoryServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ProductSubcategoryService>>();

        _subcategoryRepoMock = new Mock<IGenericRepository<ProductSubcategory>>();
        _categoryRepoMock = new Mock<IGenericRepository<ProductCategory>>();

        _uowMock.Setup(u => u.Repository<ProductSubcategory>()).Returns(_subcategoryRepoMock.Object);
        _uowMock.Setup(u => u.Repository<ProductCategory>()).Returns(_categoryRepoMock.Object);

        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // تنظیم نقش کاربر برای عبور از اعتبارسنجی
        _currentUserMock.Setup(c => c.GetCurrentUserRole()).Returns("Admin");
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(1);

        _service = new ProductSubcategoryService(
            _uowMock.Object,
            _mapperMock.Object,
            _currentUserMock.Object,
            _loggerMock.Object);
    }

    private static ProductSubcategory CreateEntity(int id = 1, string name = "Sub", int categoryId = 1)
        => new()
        {
            SubcategoryId = id,
            SubcategoryName = name,
            CategoryId = categoryId
        };

    [Fact]
    public async Task CreateAsync_ValidDto_Creates()
    {
        // Arrange
        var dto = new CreateProductSubcategoryDto
        {
            SubcategoryName = "Sub1",
            CategoryId = 1
        };
        var entity = CreateEntity(5, "Sub1", 1);
        var dtoResult = new ProductSubcategoryDto { SubcategoryId = 5, SubcategoryName = "Sub1" };

        // اعتبارسنجی: دسته‌بندی وجود داشته باشد
        var category = new ProductCategory { CategoryId = 1, CategoryName = "Cat", IsActive = true };
        _categoryRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(category);
        _categoryRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<ProductCategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _subcategoryRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<ProductSubcategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mapperMock.Setup(m => m.Map<ProductSubcategory>(dto)).Returns(entity);
        _subcategoryRepoMock.Setup(r => r.AddAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Projection
        _subcategoryRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<ProductSubcategory>>(),
                It.IsAny<Expression<Func<ProductSubcategory, ProductSubcategoryDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dtoResult);

        // Act
        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        result.SubcategoryId.Should().Be(5);
        result.SubcategoryName.Should().Be("Sub1");
    }

    [Fact]
    public async Task UpdateAsync_ValidDto_Updates()
    {
        var dto = new UpdateProductSubcategoryDto
        {
            SubcategoryId = 1,
            SubcategoryName = "Updated",
            CategoryId = 1
        };
        var entity = CreateEntity(1, "Old", 1);
        _subcategoryRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _categoryRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductCategory { CategoryId = 1, CategoryName = "Cat", IsActive = true });
        _subcategoryRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<ProductSubcategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var updatedDto = new ProductSubcategoryDto { SubcategoryId = 1, SubcategoryName = "Updated" };
        _subcategoryRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<ProductSubcategory>>(),
                It.IsAny<Expression<Func<ProductSubcategory, ProductSubcategoryDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        var result = await _service.UpdateAsync(dto);
        result.Should().NotBeNull();
        result!.SubcategoryName.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteAsync_Existing_ReturnsTrue()
    {
        var entity = CreateEntity(1, "ToDelete", 1);
        _subcategoryRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var result = await _service.DeleteAsync(1);
        result.Should().BeTrue();
    }
}