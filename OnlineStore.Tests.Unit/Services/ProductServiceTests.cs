using System.Linq.Expressions;
using Application.Common.Queries;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Specifications.Products;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;
using Application.Common.Specifications;

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

        _productService = new ProductService(
            _unitOfWorkMock.Object,
            _mapperMock.Object,
            _currentUserMock.Object,
            _loggerMock.Object);
    }

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WhenProductExistsAndActive_ReturnsProductDto()
    {
        // Arrange
        var productId = 1;
        var product = new Product
        {
            ProductId = productId,
            Name = "Test",
            SubcategoryId = 10,
            Price = 100,
            IsActive = true
        };
        var expectedDto = new ProductDto
        {
            ProductId = productId,
            Name = "Test",
            SubcategoryId = 10,
            SubcategoryName = "Cat",
            CategoryId = 5,
            CategoryName = "Main"
        };

        var repoMock = new Mock<IGenericRepository<Product>>();
        repoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Product>>(),
                It.IsAny<Expression<Func<Product, ProductDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDto);

        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(repoMock.Object);

        // Act
        var result = await _productService.GetByIdAsync(productId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedDto);
    }

    [Fact]
    public async Task GetByIdAsync_WhenProductDoesNotExist_ReturnsNull()
    {
        // Arrange
        var repoMock = new Mock<IGenericRepository<Product>>();
        repoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Product>>(),
                It.IsAny<Expression<Func<Product, ProductDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductDto?)null);

        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(repoMock.Object);

        // Act
        var result = await _productService.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_ValidDto_ReturnsProductDto()
    {
        // Arrange
        var dto = new CreateProductDto
        {
            Name = "New Product",
            Price = 200,
            SubcategoryId = 5,
            Unit = UnitOfMeasurement.Piece
        };

        var product = new Product
        {
            ProductId = 1,
            Name = dto.Name,
            Price = dto.Price,
            SubcategoryId = dto.SubcategoryId,
            Unit = dto.Unit,
            IsActive = true
        };

        var productDto = new ProductDto
        {
            ProductId = 1,
            Name = dto.Name,
            Price = dto.Price,
            SubcategoryId = dto.SubcategoryId,
            SubcategoryName = "Electronics",
            CategoryId = 10,
            CategoryName = "Main"
        };

        _mapperMock.Setup(m => m.Map<Product>(dto)).Returns(product);
        _mapperMock.Setup(m => m.Map<ProductDto>(product)).Returns(productDto);

        var subcategoryRepoMock = new Mock<IGenericRepository<ProductSubcategory>>();
        subcategoryRepoMock.Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<ProductSubcategory, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var productRepoMock = new Mock<IGenericRepository<Product>>();
        productRepoMock.Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        productRepoMock.Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock.Setup(u => u.Repository<ProductSubcategory>()).Returns(subcategoryRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(productRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _productService.CreateAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(productDto);
        productRepoMock.Verify(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsBusinessException()
    {
        // Arrange
        var dto = new CreateProductDto
        {
            Name = "Existing",
            Price = 100,
            SubcategoryId = 1
        };

        var productRepoMock = new Mock<IGenericRepository<Product>>();
        productRepoMock.Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // نام تکراری

        var subcategoryRepoMock = new Mock<IGenericRepository<ProductSubcategory>>();
        subcategoryRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<ProductSubcategory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(productRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<ProductSubcategory>()).Returns(subcategoryRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act & Assert
        await Assert.ThrowsAsync<BusinessException>(() => _productService.CreateAsync(dto));
        productRepoMock.Verify(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_ValidDto_ReturnsUpdatedProductDto()
    {
        // Arrange
        var dto = new UpdateProductDto
        {
            ProductId = 1,
            Name = "Updated",
            Price = 300,
            SubcategoryId = 2,
            Description = "new desc"
        };

        var existingProduct = new Product
        {
            ProductId = 1,
            Name = "Old",
            Price = 100,
            SubcategoryId = 1,
            IsActive = true
        };

        var productDto = new ProductDto
        {
            ProductId = 1,
            Name = "Updated",
            Price = 300,
            SubcategoryId = 2,
            SubcategoryName = "Cat",
            CategoryId = 5,
            CategoryName = "Main"
        };

        var productRepoMock = new Mock<IGenericRepository<Product>>();
        // موک برای GetByIdAsync اول (برای پیدا کردن entity)
        productRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProduct);
        // موک برای AnyAsync بررسی نام تکراری
        productRepoMock.Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // موک برای Update
        productRepoMock.Setup(r => r.Update(It.IsAny<Product>()));
        // موک برای FirstOrDefaultAsync در GetByIdAsync دوم (بعد از ذخیره)
        productRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Product>>(),
                It.IsAny<Expression<Func<Product, ProductDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(productDto);

        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(productRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // موک Mapper.Map برای به‌روزرسانی entity
        _mapperMock.Setup(m => m.Map(dto, existingProduct))
            .Callback(() =>
            {
                existingProduct.Name = dto.Name;
                existingProduct.Price = dto.Price;
                existingProduct.SubcategoryId = dto.SubcategoryId;
                existingProduct.Description = dto.Description;
            })
            .Returns(existingProduct);  // اضافه کردن Returns

        // موک برای Map دوم (نگاشت نهایی ProductDto) - در صورتی که در GetByIdAsync استفاده نشود، نیاز نیست
        // اما برای اطمینان اضافه می‌کنیم
        _mapperMock.Setup(m => m.Map<ProductDto>(It.IsAny<Product>()))
            .Returns(productDto);

        // Act
        var result = await _productService.UpdateAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(productDto);
        productRepoMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ProductNotFound_ReturnsNull()
    {
        // Arrange
        var dto = new UpdateProductDto { ProductId = 999, Name = "X", Price = 10, SubcategoryId = 1 };
        var productRepoMock = new Mock<IGenericRepository<Product>>();
        productRepoMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(productRepoMock.Object);

        // Act
        var result = await _productService.UpdateAsync(dto);

        // Assert
        result.Should().BeNull();
        productRepoMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateName_ThrowsBusinessException()
    {
        // Arrange
        var dto = new UpdateProductDto
        {
            ProductId = 1,
            Name = "DuplicateName",
            Price = 100,
            SubcategoryId = 1
        };

        var existingProduct = new Product { ProductId = 1, Name = "Original", SubcategoryId = 1, IsActive = true };
        var productRepoMock = new Mock<IGenericRepository<Product>>();
        productRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProduct);
        productRepoMock.Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // نام تکراری

        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(productRepoMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<BusinessException>(() => _productService.UpdateAsync(dto));
        productRepoMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WhenProductExistsAndActive_ReturnsTrueAndSoftDeletes()
    {
        // Arrange
        var productId = 1;
        var product = new Product { ProductId = productId, Name = "Test", SubcategoryId = 1, IsActive = true };
        var productRepoMock = new Mock<IGenericRepository<Product>>();
        productRepoMock.Setup(r => r.GetByIdAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        productRepoMock.Setup(r => r.Update(It.IsAny<Product>()));

        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(productRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _productService.DeleteAsync(productId);

        // Assert
        result.Should().BeTrue();
        product.IsActive.Should().BeFalse();
        productRepoMock.Verify(r => r.Update(product), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenProductNotFound_ReturnsFalse()
    {
        // Arrange
        var productRepoMock = new Mock<IGenericRepository<Product>>();
        productRepoMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Product?)null);

        _unitOfWorkMock.Setup(u => u.Repository<Product>()).Returns(productRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _productService.DeleteAsync(999);

        // Assert
        result.Should().BeFalse();
        productRepoMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}