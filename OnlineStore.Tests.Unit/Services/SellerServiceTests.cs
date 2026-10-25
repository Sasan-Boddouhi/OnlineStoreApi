using System.Linq.Expressions;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.Seller;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace OnlineStore.Tests.Unit.Services;

/// <summary>
/// Phase 2 — قواعد کسب‌وکار فروشنده و پیشنهاد فروش.
/// </summary>
public class SellerServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<SellerService>> _loggerMock;

    private readonly Mock<IGenericRepository<Seller>> _sellerRepoMock;
    private readonly Mock<IGenericRepository<SellerProduct>> _listingRepoMock;
    private readonly Mock<IGenericRepository<Product>> _productRepoMock;
    private readonly Mock<IGenericRepository<ProductVariant>> _variantRepoMock;
    private readonly Mock<IGenericRepository<User>> _userRepoMock;
    private readonly Mock<IGenericRepository<Warehouse>> _warehouseRepoMock;

    private readonly SellerService _service;

    public SellerServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<SellerService>>();

        _sellerRepoMock = new Mock<IGenericRepository<Seller>>();
        _listingRepoMock = new Mock<IGenericRepository<SellerProduct>>();
        _productRepoMock = new Mock<IGenericRepository<Product>>();
        _variantRepoMock = new Mock<IGenericRepository<ProductVariant>>();
        _userRepoMock = new Mock<IGenericRepository<User>>();
        _warehouseRepoMock = new Mock<IGenericRepository<Warehouse>>();

        _uowMock.Setup(u => u.Repository<Seller>()).Returns(_sellerRepoMock.Object);
        _uowMock.Setup(u => u.Repository<SellerProduct>()).Returns(_listingRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Product>()).Returns(_productRepoMock.Object);
        _uowMock.Setup(u => u.Repository<ProductVariant>()).Returns(_variantRepoMock.Object);
        _uowMock.Setup(u => u.Repository<User>()).Returns(_userRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Warehouse>()).Returns(_warehouseRepoMock.Object);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _service = new SellerService(_uowMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    private static User NewUser(int id) => new()
    {
        UserId = id,
        FirstName = "S",
        LastName = "B",
        PasswordHash = "hash",
        UserType = UserType.Customer
    };

    private void SetupSellerAny(params bool[] sequence)
    {
        var setup = _sellerRepoMock.SetupSequence(
            r => r.AnyAsync(It.IsAny<Expression<Func<Seller, bool>>>(), It.IsAny<CancellationToken>()));
        foreach (var value in sequence)
            setup.ReturnsAsync(value);
    }

    // ---------------- Create (Seller) ----------------

    [Fact]
    public async Task CreateAsync_UserNotFound_ThrowsCode()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateAsync(new CreateSellerDto { UserId = 5, StoreName = "Shop" }));

        ex.Code.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task CreateAsync_UserAlreadyHasShop_ThrowsCode()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewUser(5));
        SetupSellerAny(true); // UserId یکتاست ⇒ رابطه ۱:۱

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateAsync(new CreateSellerDto { UserId = 5, StoreName = "Shop" }));

        ex.Code.Should().Be("SELLER_ALREADY_EXISTS");
    }

    [Fact]
    public async Task CreateAsync_DuplicateStoreName_ThrowsCode()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewUser(5));
        SetupSellerAny(false, true);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateAsync(new CreateSellerDto { UserId = 5, StoreName = "Shop" }));

        ex.Code.Should().Be("SELLER_STORE_NAME_DUPLICATE");
    }

    [Fact]
    public async Task CreateAsync_DuplicateSlug_ThrowsCode()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewUser(5));
        SetupSellerAny(false, false, true);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateAsync(new CreateSellerDto { UserId = 5, StoreName = "Shop", Slug = "shop" }));

        ex.Code.Should().Be("SELLER_SLUG_DUPLICATE");
    }

    [Fact]
    public async Task CreateAsync_Valid_AlwaysStartsPending()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewUser(5));
        SetupSellerAny(false, false, false);

        // حتی اگر AutoMapper چیزی غیر از Pending بدهد، سرویس باید آن را بازنویسی کند.
        _mapperMock.Setup(m => m.Map<Seller>(It.IsAny<CreateSellerDto>()))
            .Returns(new Seller { UserId = 5, StoreName = "Shop", Status = SellerStatus.Approved });

        Seller? added = null;
        _sellerRepoMock.Setup(r => r.AddAsync(It.IsAny<Seller>(), It.IsAny<CancellationToken>()))
            .Callback<Seller, CancellationToken>((s, _) => added = s)
            .Returns(Task.CompletedTask);

        await _service.CreateAsync(new CreateSellerDto { UserId = 5, StoreName = "Shop" });

        added.Should().NotBeNull();
        added!.Status.Should().Be(SellerStatus.Pending);
        added.IsActive.Should().BeTrue();
    }

    // ---------------- Create (Listing) ----------------

    [Fact]
    public async Task CreateListingAsync_NonPositivePrice_ThrowsCode()
    {
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateListingAsync(new CreateSellerProductDto { SellerId = 1, VariantId = 1, Price = 0 }));

        ex.Code.Should().Be("INVALID_PRICE");
    }

    [Fact]
    public async Task CreateListingAsync_SellerNotApproved_ThrowsCode()
    {
        _sellerRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Seller { SellerId = 1, StoreName = "Shop", Status = SellerStatus.Pending });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateListingAsync(new CreateSellerProductDto { SellerId = 1, VariantId = 1, Price = 10 }));

        ex.Code.Should().Be("SELLER_NOT_APPROVED");
    }

    [Fact]
    public async Task CreateListingAsync_DuplicateForSameVariant_ThrowsCode()
    {
        _sellerRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Seller { SellerId = 1, StoreName = "Shop", Status = SellerStatus.Approved, IsActive = true });
        _variantRepoMock.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductVariant { VariantId = 2, ProductId = 2, IsActive = true });
        _listingRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SellerProduct, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateListingAsync(new CreateSellerProductDto { SellerId = 1, VariantId = 2, Price = 10 }));

        ex.Code.Should().Be("LISTING_ALREADY_EXISTS");
    }

    [Fact]
    public async Task CreateListingAsync_UnknownVariant_ThrowsCode()
    {
        _sellerRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Seller { SellerId = 1, StoreName = "Shop", Status = SellerStatus.Approved, IsActive = true });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateListingAsync(new CreateSellerProductDto { SellerId = 1, VariantId = 777, Price = 10 }));

        ex.Code.Should().Be("VARIANT_NOT_FOUND");
    }

    [Fact]
    public async Task CreateListingAsync_InactiveVariant_ThrowsCode()
    {
        _sellerRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Seller { SellerId = 1, StoreName = "Shop", Status = SellerStatus.Approved, IsActive = true });
        _variantRepoMock.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductVariant { VariantId = 3, ProductId = 2, IsActive = false });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateListingAsync(new CreateSellerProductDto { SellerId = 1, VariantId = 3, Price = 10 }));

        ex.Code.Should().Be("VARIANT_INACTIVE");
    }

    [Fact]
    public async Task CreateListingAsync_Valid_CreatesListing()
    {
        _sellerRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Seller { SellerId = 1, StoreName = "Shop", Status = SellerStatus.Approved, IsActive = true });
        _variantRepoMock.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductVariant { VariantId = 2, ProductId = 2, IsActive = true });
        _listingRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SellerProduct, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mapperMock.Setup(m => m.Map<SellerProduct>(It.IsAny<CreateSellerProductDto>()))
            .Returns(new SellerProduct { SellerId = 1, VariantId = 2, Price = 99 });

        SellerProduct? added = null;
        _listingRepoMock.Setup(r => r.AddAsync(It.IsAny<SellerProduct>(), It.IsAny<CancellationToken>()))
            .Callback<SellerProduct, CancellationToken>((l, _) => added = l)
            .Returns(Task.CompletedTask);

        await _service.CreateListingAsync(new CreateSellerProductDto { SellerId = 1, VariantId = 2, Price = 99 });

        added.Should().NotBeNull();
        added!.Price.Should().Be(99);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------- Delete ----------------

    [Fact]
    public async Task DeleteAsync_SellerHasWarehouses_ThrowsCode()
    {
        _sellerRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Seller { SellerId = 1, StoreName = "Shop" });
        _listingRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<SellerProduct, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _warehouseRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Warehouse, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.DeleteAsync(1));

        ex.Code.Should().Be("SELLER_HAS_DEPENDENCIES");
        _sellerRepoMock.Verify(r => r.Delete(It.IsAny<Seller>()), Times.Never);
    }

    [Fact]
    public async Task GetListingsAsync_WithSellerFilter_UsesSpecification()
    {
        _listingRepoMock
            .Setup(r => r.ListAsync(It.IsAny<Spec<SellerProduct>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SellerProduct>());

        var result = await _service.GetListingsAsync(sellerId: 7);

        result.Should().BeEmpty();
        _listingRepoMock.Verify(
            r => r.ListAsync(It.IsAny<Spec<SellerProduct>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}