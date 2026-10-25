using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Review;
using BusinessLogic.DTOs.Seller;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 7 — R26 (واریانت پیش‌فرض)، R27 (تنوع در سبد)، R22 (میانگین امتیاز)
/// و داشبورد فروشنده.
/// </summary>
public class Phase7SellerDashboardTests : BaseIntegrationTest
{
    private IProductService ProductService => GetService<IProductService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private ICartService CartService => GetService<ICartService>();
    private IOrderService OrderService => GetService<IOrderService>();
    private IReviewService ReviewService => GetService<IReviewService>();
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase7SellerDashboardTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private void SetCurrentUser(int userId, string role = "Seller")
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    // ==================== R26 ====================

    [Fact]
    public async Task CreateProduct_AutomaticallyCreatesDefaultVariant()
    {
        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"AutoVariant-{Guid.NewGuid():N}",
            Price = 100,
            SubcategoryId = 1
        });

        var variants = await ProductService.GetVariantsAsync(product.ProductId);

        variants.Should().HaveCount(1);
        variants[0].Name.Should().Be("Default");
        variants[0].ProductId.Should().Be(product.ProductId);
        variants[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task NewProduct_CanImmediatelyReceiveAListing()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"Sellable-{Guid.NewGuid():N}", Price = 50, SubcategoryId = 1
        });

        var variant = (await ProductService.GetVariantsAsync(product.ProductId)).Single();

        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 45,
            Status = SellerProductStatus.Active
        });

        listing.ProductId.Should().Be(product.ProductId);
    }

    // ==================== R27 ====================

    [Fact]
    public async Task GetCart_ReturnsVariantNameForItems()
    {
        var (buyerUser, _) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        var listing = await CreateListingWithStockAsync(seller, "256GB Black", 100m);

        SetCurrentUser(buyerUser.UserId);

        await CartService.AddItemAsync(
            buyerUser.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });

        var cart = await CartService.GetCartAsync(buyerUser.UserId, null);

        cart.Items.Should().HaveCount(1);
        cart.Items[0].VariantName.Should().Be("256GB Black");
        cart.Items[0].ProductName.Should().StartWith("P7-");
    }

    [Fact]
    public async Task GetCart_DistinguishesTwoVariantsOfTheSameProduct()
    {
        var (buyerUser, _) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"Cart-{Guid.NewGuid():N}", Price = 100, SubcategoryId = 1
        });

        var defaultVariant = (await ProductService.GetVariantsAsync(product.ProductId)).Single();

        var second = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId,
            Name = "512GB White",
            Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var first = await CreateListingForVariantAsync(seller, defaultVariant.VariantId, "Default", 100m);
        var other = await CreateListingForVariantAsync(seller, second.VariantId, "512GB White", 150m);

        SetCurrentUser(buyerUser.UserId);

        await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto { ListingId = first.ListingId, Quantity = 1 });
        await CartService.AddItemAsync(buyerUser.UserId, null, new AddCartItemDto { ListingId = other.ListingId, Quantity = 1 });

        var cart = await CartService.GetCartAsync(buyerUser.UserId, null);

        cart.Items.Should().HaveCount(2);
        cart.Items.Select(i => i.VariantName).Should().BeEquivalentTo(new[] { "Default", "512GB White" });
        // بدون VariantName هر دو آیتم دقیقاً یکسان به‌نظر می‌رسیدند.
        cart.Items.Select(i => i.VariantName).Distinct().Should().HaveCount(2);
    }

    // ==================== R22 ====================

    [Fact]
    public async Task GetProduct_CalculatesAverageRatingFromApprovedReviewsOnly()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);
        var listing = await CreateListingWithStockAsync(seller, "Default", 100m);
        var productId = listing.ProductId;

        // خریدار اول نظر ۵ می‌دهد و تأیید می‌شود.
        var (_, firstReviewId) = await CreateDeliveredOrderAsync(listing, 5);

        // خریدار دوم خرید می‌کند ولی نظر نمی‌دهد (rating = 0)،
        // تا بتوانیم یک نظر تأییدنشده با امتیاز ۱ بسازیم.
        var (secondBuyerId, _) = await CreateDeliveredOrderAsync(listing, 0);

        var unapproved = await ReviewService.AddProductReviewAsync(
            secondBuyerId, productId, new CreateProductReviewDto { Rating = 1 });

        var product = await ProductService.GetByIdAsync(productId);

        product.Should().NotBeNull();
        product!.ReviewCount.Should().Be(1);
        product.AverageRating.Should().BeApproximately(5d, 0.01);

        // نظر تأییدنشده نباید روی میانگین اثر بگذارد.
        await ReviewService.ApproveProductReviewAsync(unapproved.ProductReviewId);

        var updated = await ProductService.GetByIdAsync(productId);
        updated!.ReviewCount.Should().Be(2);
        // (۵ + ۱) / ۲ = ۳
        updated.AverageRating.Should().BeApproximately(3d, 0.01);
    }

    [Fact]
    public async Task GetSeller_CalculatesAverageRatingFromApprovedReviewsOnly()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);
        var listing = await CreateListingWithStockAsync(seller, "Default", 100m);

        var (buyerAId, _) = await CreateDeliveredOrderAsync(listing, 5);
        var review = await ReviewService.AddSellerReviewAsync(
            buyerAId, seller.SellerId, new CreateSellerReviewDto { Rating = 4 });

        var before = await SellerService.GetByIdAsync(seller.SellerId);
        before.ReviewCount.Should().Be(0, "نظر تأییدنشده شمرده نمی‌شود");

        await ReviewService.ApproveSellerReviewAsync(review.SellerReviewId);

        var after = await SellerService.GetByIdAsync(seller.SellerId);
        after.ReviewCount.Should().Be(1);
        after.AverageRating.Should().BeApproximately(4d, 0.01);
    }

    // ==================== داشبورد ====================

    [Fact]
    public async Task Dashboard_Summary_CountsOnlyLiveOrders()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        SetCurrentUser(sellerUser.UserId, "Seller");

        var listing = await CreateListingWithStockAsync(seller, "Default", 100m);

        await CreateDeliveredOrderAsync(listing, 5, quantity: 2);

        // ساخت سفارش، کاربر جاری را به خریدار تغییر می‌دهد؛ باید به فروشنده برگردد.
        SetCurrentUser(sellerUser.UserId, "Seller");

        var summary = await DashboardService.GetSalesSummaryAsync(seller.SellerId, null, null);

        summary.SellerId.Should().Be(seller.SellerId);
        summary.TotalOrders.Should().Be(1);
        summary.TotalSales.Should().Be(200m);
        summary.CommissionPaid.Should().BeGreaterThan(0m);
        summary.NetEarnings.Should().Be(summary.TotalSales - summary.CommissionPaid);
    }

    [Fact]
    public async Task Dashboard_TopProducts_RanksByQuantitySold()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        SetCurrentUser(sellerUser.UserId, "Seller");

        var listing = await CreateListingWithStockAsync(seller, "Hot Item", 30m);

        await CreateDeliveredOrderAsync(listing, 5, quantity: 3);

        SetCurrentUser(sellerUser.UserId, "Seller");

        var top = await DashboardService.GetTopProductsAsync(seller.SellerId, pageNumber: 1, pageSize: 5);

        top.Items.Should().HaveCount(1);
        top.Items[0].QuantitySold.Should().Be(3);
        top.Items[0].Revenue.Should().Be(90m);
        top.Items[0].VariantName.Should().Be("Hot Item");
        top.Items[0].ProductName.Should().StartWith("P7-");
    }

    [Fact]
    public async Task Dashboard_RecentOrders_ReturnsProductAndVariantNames()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        SetCurrentUser(sellerUser.UserId, "Seller");

        var listing = await CreateListingWithStockAsync(seller, "Recent Variant", 20m);
        await CreateDeliveredOrderAsync(listing, 5, quantity: 1);

        SetCurrentUser(sellerUser.UserId, "Seller");

        var recent = await DashboardService.GetRecentOrdersAsync(seller.SellerId, pageNumber: 1, pageSize: 5);

        recent.Items.Should().HaveCount(1);
        recent.Items[0].ProductName.Should().StartWith("P7-");
        recent.Items[0].VariantName.Should().Be("Recent Variant");
        recent.Items[0].LineTotal.Should().Be(20m);
        recent.Items[0].OrderStatus.Should().Be(nameof(OrderStatus.Delivered));
    }

    [Fact]
    public async Task Dashboard_AnotherSellerCannotReadForeignDashboard()
    {
        var (ownerUser, _) = await CreateCustomerAsync();
        var (intruderUser, _) = await CreateCustomerAsync();

        var owner = await CreateSellerAsync(ownerUser.UserId);

        SetCurrentUser(intruderUser.UserId, "Seller");

        var act = () => DashboardService.GetSalesSummaryAsync(owner.SellerId, null, null);

        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.And.Code.Should().Be("DASHBOARD_FORBIDDEN");
    }

    [Fact]
    public async Task Dashboard_AdminCanReadAnySellerDashboard()
    {
        var (ownerUser, _) = await CreateCustomerAsync();
        var (adminUser, _) = await CreateCustomerAsync();

        var owner = await CreateSellerAsync(ownerUser.UserId);

        SetCurrentUser(adminUser.UserId, "Admin");

        var summary = await DashboardService.GetSalesSummaryAsync(owner.SellerId, null, null);

        summary.SellerId.Should().Be(owner.SellerId);
        summary.TotalOrders.Should().Be(0);
    }

    [Fact]
    public async Task Dashboard_InvalidDateRange_IsRejected()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        SetCurrentUser(sellerUser.UserId, "Seller");

        var act = () => DashboardService.GetSalesSummaryAsync(
            seller.SellerId, DateTime.UtcNow, DateTime.UtcNow.AddDays(-1));

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("INVALID_DATE_RANGE");
    }

    // ==================== Helpers ====================

    private async Task<(User user, Customer customer)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "P7",
            LastName = "User",
            PhoneNumber = $"0913{new Random().Next(1000000, 9999999)}",
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

    private async Task<Seller> CreateSellerAsync(int ownerUserId)
    {
        var seller = new Seller
        {
            UserId = ownerUserId,
            StoreName = $"P7Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();
        return seller;
    }

    private async Task<ProductDto> CreateProductAsync()
        => await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"P7-{Guid.NewGuid():N}", Price = 100, SubcategoryId = 1
        });

    private async Task<SellerProductDto> CreateListingWithStockAsync(Seller seller, string variantName, decimal price)
    {
        var product = await CreateProductAsync();
        var variant = (await ProductService.GetVariantsAsync(product.ProductId)).Single();

        if (variantName != variant.Name)
        {
            var renamed = await ProductService.CreateVariantAsync(new CreateProductVariantDto
            {
                ProductId = product.ProductId,
                Name = variantName,
                Sku = $"SKU-{Guid.NewGuid():N}"
            });
            return await CreateListingForVariantAsync(seller, renamed.VariantId, variantName, price);
        }

        return await CreateListingForVariantAsync(seller, variant.VariantId, variantName, price);
    }

    private async Task<SellerProductDto> CreateListingForVariantAsync(
        Seller seller, int variantId, string variantName, decimal price)
    {
        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variantId,
            Price = price,
            Status = SellerProductStatus.Active
        });

        await EnsureStockAsync(seller, listing.ListingId);

        return listing;
    }

    private async Task EnsureStockAsync(Seller seller, int listingId)
    {
        var warehouse = await DbContext.Warehouse.FirstOrDefaultAsync(w => w.SellerId == seller.SellerId);

        if (warehouse is null)
        {
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
                RecipientFirstName = "P7",
                RecipientLastName = "Shop",
                UserId = seller.UserId
            };
            DbContext.Address.Add(address);
            await DbContext.SaveChangesAsync();

            warehouse = new Warehouse
            {
                Name = $"W-{Guid.NewGuid():N}",
                AddressId = address.AddressId,
                SellerId = seller.SellerId
            };
            DbContext.Warehouse.Add(warehouse);
            await DbContext.SaveChangesAsync();
        }

        DbContext.Inventory.Add(new Inventory(listingId, warehouse.WarehouseId, 100));
        await DbContext.SaveChangesAsync();
    }

    /// <summary>
    /// یک خرید تحویل‌شده می‌سازد و نظر تأییدشده ثبت می‌کند.
    /// شناسهٔ کاربر خریدار برگردانده می‌شود.
    /// </summary>
    private async Task<(int BuyerUserId, int ReviewId)> CreateDeliveredOrderAsync(
        SellerProductDto listing, int rating, decimal quantity = 1)
    {
        var (buyerUser, buyer) = await CreateCustomerAsync();

        SetCurrentUser(buyerUser.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(buyer.CustomerId, "P7 Buyer", "Tehran", "09120000000");

        await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto
        {
            ListingId = listing.ListingId,
            Quantity = quantity
        });

        await OrderService.ConfirmOrderAsync(order.OrderId);
        await OrderService.MarkDeliveredAsync(order.OrderId);

        var reviewId = 0;

        if (rating > 0)
        {
            var review = await ReviewService.AddProductReviewAsync(
                buyerUser.UserId, listing.ProductId, new CreateProductReviewDto { Rating = rating });
            await ReviewService.ApproveProductReviewAsync(review.ProductReviewId);
            reviewId = review.ProductReviewId;
        }

        return (buyerUser.UserId, reviewId);
    }
}