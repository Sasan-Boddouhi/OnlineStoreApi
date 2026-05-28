using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Specifications.Products;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace OnlineStore.Tests.Unit.Services;

public class ProductServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<ILogger<ProductService>> _loggerMock;
    private readonly ProductService _productService;

    public ProductServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<ProductService>>();
        _productService = new ProductService(_unitOfWorkMock.Object, _mapperMock.Object, _currentUserMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsBusinessException()
    {
        // Arrange
        var dto = new CreateProductDto { Name = "Duplicate", Price = 100, SubcategoryId = 1 };
        var mockRepo = new Mock<IGenericRepository<Product>>();
        mockRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>>(), default))
            .ReturnsAsync(true);
        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(mockRepo.Object);

        // Act & Assert
        await Assert.ThrowsAsync<BusinessException>(() => _productService.CreateAsync(dto));
    }

    [Fact]
    public async Task DeleteAsync_ProductNotFound_ReturnsFalse()
    {
        // Arrange
        var mockRepo = new Mock<IGenericRepository<Product>>();
        mockRepo.Setup(r => r.GetByIdAsync(999, default)).ReturnsAsync((Product)null);
        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(mockRepo.Object);

        // Act
        var result = await _productService.DeleteAsync(999);

        // Assert
        result.Should().BeFalse();
    }
}