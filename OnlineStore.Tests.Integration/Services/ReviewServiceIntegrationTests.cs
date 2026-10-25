using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Review;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 5 — نظرات. قانون اصلی: نظر فقط با خرید تحویل‌شده.
/// </summary>
public class ReviewServiceIntegrationTests : BaseIntegrationTest
{
    private IReviewService ReviewService => GetService<IReviewService>();
    private IOrderService OrderService => GetService<IOrderService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public ReviewServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    /// <summary>Phase 6: هر Listing به یک واریانت وصل است، پس تست‌ها یک واریانت پیش‌فرض می‌سازند.</summary>
    private async Task<ProductVariant> CreateVariantAsync(int productId, string? name = null)
    {
        var variant = new ProductVariant
        {
            ProductId = productId,
            Name = name ?? "Default",
            Sku = $"SKU-{Guid.NewGuid():N}"
        };

        DbContext.ProductVariant.Add(variant);
        await DbContext.SaveChangesAsync();
        return variant;
    }

    private void SetCurrentUser(int userId)
        => ((FakeCurrentUserService)GetService<ICurrentUserService>()).CurrentUserId = userId;

    private async Task<(User user, Customer customer)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "Rev",
            LastName = "Buyer",
            PhoneNumber = $"0915{new Random().Next(1000000, 9999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();

        return (user, customer);
    }

    private async Task<Product> CreateProductAsync()
    {
        var product = new Product { Name = $"Rev-{Guid.NewGuid():N}", Price = 100, SubcategoryId = 1 };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();
        return product;
    }

    private async Task<(Seller seller, SellerProduct listing)> CreateListingAsync(int ownerUserId, int productId, decimal price)
    {
        var seller = new Seller
        {
            UserId = ownerUserId,
            StoreName = $"RevShop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        var variant = await CreateVariantAsync(productId);

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = price,
            Status = SellerProductStatus.Active
        };
        DbContext.SellerProduct.Add(listing);
        await DbContext.SaveChangesAsync();

        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "Rec",
            RecipientLastName = "Ipient",
            UserId = ownerUserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            Name = $"W-{Guid.NewGuid():N}",
            AddressId = address.AddressId,
            SellerId = seller.SellerId
        };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, 100));
        await DbContext.SaveChangesAsync();

        return (seller, listing);
    }

    /// <summary>خرید کامل تا وضعیت Delivered می‌سازد.</summary>
    private async Task<(User buyer, Customer customer, Seller seller, Product product)> CreateDeliveredPurchaseAsync()
    {
        var (buyer, customer) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var (seller, listing) = await CreateListingAsync(sellerUser.UserId, product.ProductId, product.Price);

        SetCurrentUser(buyer.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Buyer", "Tehran", "09120000000");
        await OrderService.AddItemToOrderAsync(
            order.OrderId, new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 1 });
        await OrderService.ConfirmOrderAsync(order.OrderId);
        await OrderService.MarkDeliveredAsync(order.OrderId);

        return (buyer, customer, seller, product);
    }

    // ==================== محصول ====================

    [Fact]
    public async Task AddProductReview_AfterDeliveredPurchase_Succeeds_ButIsNotApprovedByDefault()
    {
        var (buyer, _, _, product) = await CreateDeliveredPurchaseAsync();

        var review = await ReviewService.AddProductReviewAsync(
            buyer.UserId, product.ProductId, new CreateProductReviewDto { Rating = 5, Comment = "عالی بود" });

        review.Rating.Should().Be(5);
        review.Comment.Should().Be("عالی بود");
        review.IsApproved.Should().BeFalse();

        var stored = await DbContext.ProductReview.SingleAsync(r => r.ProductReviewId == review.ProductReviewId);
        stored.OrderItemId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AddProductReview_WithoutAnyPurchase_IsRejected()
    {
        var (stranger, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();

        var act = () => ReviewService.AddProductReviewAsync(
            stranger.UserId, product.ProductId, new CreateProductReviewDto { Rating = 5 });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("REVIEW_REQUIRES_DELIVERED_PURCHASE");
    }

    [Fact]
    public async Task AddProductReview_WhenOrderNotDeliveredYet_IsRejected()
    {
        var (buyer, customer) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var (_, listing) = await CreateListingAsync(sellerUser.UserId, product.ProductId, product.Price);

        SetCurrentUser(buyer.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Buyer", "Tehran", "09120000000");
        await OrderService.AddItemToOrderAsync(
            order.OrderId, new AddOrderItemDto { ListingId = listing.ListingId, Quantity = 1 });
        await OrderService.ConfirmOrderAsync(order.OrderId);   // Processing، نه Delivered

        var act = () => ReviewService.AddProductReviewAsync(
            buyer.UserId, product.ProductId, new CreateProductReviewDto { Rating = 4 });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("REVIEW_REQUIRES_DELIVERED_PURCHASE");
    }

    [Fact]
    public async Task AddProductReview_Twice_IsRejected()
    {
        var (buyer, _, _, product) = await CreateDeliveredPurchaseAsync();

        await ReviewService.AddProductReviewAsync(
            buyer.UserId, product.ProductId, new CreateProductReviewDto { Rating = 5 });

        var act = () => ReviewService.AddProductReviewAsync(
            buyer.UserId, product.ProductId, new CreateProductReviewDto { Rating = 1 });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("REVIEW_ALREADY_EXISTS");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task AddProductReview_OutOfRangeRating_IsRejected(int rating)
    {
        var (buyer, _, _, product) = await CreateDeliveredPurchaseAsync();

        var act = () => ReviewService.AddProductReviewAsync(
            buyer.UserId, product.ProductId, new CreateProductReviewDto { Rating = rating });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("INVALID_RATING");
    }

    [Fact]
    public async Task GetProductReviews_ReturnsOnlyApprovedReviews()
    {
        var (buyer, _, _, product) = await CreateDeliveredPurchaseAsync();

        var review = await ReviewService.AddProductReviewAsync(
            buyer.UserId, product.ProductId, new CreateProductReviewDto { Rating = 5 });

        (await ReviewService.GetProductReviewsAsync(product.ProductId)).Should().BeEmpty();

        await ReviewService.ApproveProductReviewAsync(review.ProductReviewId);

        var approved = (await ReviewService.GetProductReviewsAsync(product.ProductId)).ToList();

        approved.Should().HaveCount(1);
        approved[0].ProductReviewId.Should().Be(review.ProductReviewId);
        approved[0].CustomerName.Should().Be("Rev Buyer");
    }

    // ==================== فروشنده ====================

    [Fact]
    public async Task AddSellerReview_AfterDeliveredPurchase_Succeeds()
    {
        var (buyer, _, seller, _) = await CreateDeliveredPurchaseAsync();

        var review = await ReviewService.AddSellerReviewAsync(
            buyer.UserId, seller.SellerId, new CreateSellerReviewDto { Rating = 4, Comment = "ارسال سریع" });

        review.SellerId.Should().Be(seller.SellerId);
        review.Rating.Should().Be(4);
        review.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task AddSellerReview_WithoutPurchase_IsRejected()
    {
        var (stranger, _) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var (seller, _) = await CreateListingAsync(sellerUser.UserId, product.ProductId, product.Price);

        var act = () => ReviewService.AddSellerReviewAsync(
            stranger.UserId, seller.SellerId, new CreateSellerReviewDto { Rating = 1 });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("REVIEW_REQUIRES_DELIVERED_PURCHASE");
    }

    [Fact]
    public async Task AddSellerReview_Twice_IsRejected()
    {
        var (buyer, _, seller, _) = await CreateDeliveredPurchaseAsync();

        await ReviewService.AddSellerReviewAsync(
            buyer.UserId, seller.SellerId, new CreateSellerReviewDto { Rating = 5 });

        var act = () => ReviewService.AddSellerReviewAsync(
            buyer.UserId, seller.SellerId, new CreateSellerReviewDto { Rating = 2 });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("REVIEW_ALREADY_EXISTS");
    }
}