using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class ReviewsControllerIntegrationTests : ControllerIntegrationTestBase
{
    public ReviewsControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<(string Token, int UserId)> CreateCustomerAndTokenAsync()
    {
        var phone = $"0912{Random.Shared.Next(1000000, 9999999):D7}";
        var registerData = new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "Customer",
            LastName = "User",
            DeviceId = $"device-{Guid.NewGuid():N}",
            DateOfBirth = "1370/01/01"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", registerData);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();
        var db = GetService<AppDbContext>();
        var user = await db.User.FirstAsync(u => u.PhoneNumber == phone);

        return (result!.AccessToken, user.UserId);
    }

    private async Task<ProductReview> CreateProductReviewAsync(bool approved = true)
    {
        var db = GetService<AppDbContext>();
        var user = new User
        {
            PhoneNumber = $"0912{Random.Shared.Next(1000000, 9999999):D7}",
            FirstName = "Buyer",
            LastName = "Test",
            UserType = UserType.Customer,
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(user);
        await db.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        db.Customer.Add(customer);

        var product = new Product { Name = "P-" + Guid.NewGuid().ToString("N"), Price = 100, SubcategoryId = 1 };
        db.Product.Add(product);
        await db.SaveChangesAsync();

        var variant = new ProductVariant { ProductId = product.ProductId, Name = "V", Sku = "SKU-" + Guid.NewGuid().ToString("N") };
        db.ProductVariant.Add(variant);
        await db.SaveChangesAsync();

        var sellerUser = new User
        {
            PhoneNumber = $"0912{Random.Shared.Next(1000000, 9999999):D7}",
            FirstName = "Shop",
            LastName = "Owner",
            UserType = UserType.Customer,
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(sellerUser);
        await db.SaveChangesAsync();

        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = "Store-" + Guid.NewGuid().ToString("N"),
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 100m,
            Status = SellerProductStatus.Active
        };
        db.SellerProduct.Add(listing);
        await db.SaveChangesAsync();

        var order = new Order
        {
            CustomerId = customer.CustomerId,
            ShippingFullName = "F",
            ShippingAddress = "A",
            ShippingPhoneNumber = "09120000000"
        };
        db.Order.Add(order);
        await db.SaveChangesAsync();

        var orderItem = new OrderItem(product.ProductId, 1, 100m)
        {
            OrderId = order.OrderId,
            ListingId = listing.ListingId,
            VariantId = variant.VariantId
        };
        db.OrderItem.Add(orderItem);
        await db.SaveChangesAsync();

        var review = new ProductReview
        {
            ProductId = product.ProductId,
            CustomerId = customer.CustomerId,
            OrderItemId = orderItem.OrderItemId,
            Rating = 5,
            Comment = "Excellent",
            IsApproved = approved
        };
        db.ProductReview.Add(review);
        await db.SaveChangesAsync();

        return review;
    }

    private async Task<SellerReview> CreateSellerReviewAsync(bool approved = true)
    {
        var db = GetService<AppDbContext>();
        var user = new User
        {
            PhoneNumber = $"0912{Random.Shared.Next(1000000, 9999999):D7}",
            FirstName = "SellerBuyer",
            LastName = "Test",
            UserType = UserType.Customer,
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(user);
        await db.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        db.Customer.Add(customer);

        var sellerUser = new User
        {
            PhoneNumber = $"0912{Random.Shared.Next(1000000, 9999999):D7}",
            FirstName = "Shop",
            LastName = "Owner",
            UserType = UserType.Customer,
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(sellerUser);
        await db.SaveChangesAsync();

        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = "Store-" + Guid.NewGuid().ToString("N"),
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        var review = new SellerReview
        {
            SellerId = seller.SellerId,
            CustomerId = customer.CustomerId,
            Rating = 5,
            Comment = "Fast shipping",
            IsApproved = approved
        };
        db.SellerReview.Add(review);
        await db.SaveChangesAsync();

        return review;
    }

    [Fact]
    public async Task RejectProductReview_Admin_ReturnsNoContent()
    {
        var adminToken = await GetAdminTokenAsync();
        var review = await CreateProductReviewAsync(approved: true);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await Client.PostAsync($"/api/v1/reviews/product/{review.ProductReviewId}/reject", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var db = GetService<AppDbContext>();
        var updated = await db.ProductReview.AsNoTracking().FirstOrDefaultAsync(r => r.ProductReviewId == review.ProductReviewId);
        updated!.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task RejectProductReview_NonAdmin_ReturnsForbidden()
    {
        var (token, _) = await CreateCustomerAndTokenAsync();
        var review = await CreateProductReviewAsync(approved: true);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PostAsync($"/api/v1/reviews/product/{review.ProductReviewId}/reject", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteProductReview_Admin_ReturnsNoContent()
    {
        var adminToken = await GetAdminTokenAsync();
        var review = await CreateProductReviewAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await Client.DeleteAsync($"/api/v1/reviews/product/{review.ProductReviewId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var db = GetService<AppDbContext>();
        var deleted = await db.ProductReview.AsNoTracking().FirstOrDefaultAsync(r => r.ProductReviewId == review.ProductReviewId);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task RejectSellerReview_Admin_ReturnsNoContent()
    {
        var adminToken = await GetAdminTokenAsync();
        var review = await CreateSellerReviewAsync(approved: true);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await Client.PostAsync($"/api/v1/reviews/seller/{review.SellerReviewId}/reject", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var db = GetService<AppDbContext>();
        var updated = await db.SellerReview.AsNoTracking().FirstOrDefaultAsync(r => r.SellerReviewId == review.SellerReviewId);
        updated!.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSellerReview_Admin_ReturnsNoContent()
    {
        var adminToken = await GetAdminTokenAsync();
        var review = await CreateSellerReviewAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await Client.DeleteAsync($"/api/v1/reviews/seller/{review.SellerReviewId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var db = GetService<AppDbContext>();
        var deleted = await db.SellerReview.AsNoTracking().FirstOrDefaultAsync(r => r.SellerReviewId == review.SellerReviewId);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task GetProductReviews_AllowAnonymous_ReturnsOk()
    {
        var review = await CreateProductReviewAsync(approved: true);

        Client.DefaultRequestHeaders.Authorization = null;
        var response = await Client.GetAsync($"/api/v1/reviews/product/{review.ProductId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<BusinessLogic.DTOs.Review.ProductReviewDto>>();
        list.Should().NotBeNull();
        list!.Should().Contain(r => r.ProductReviewId == review.ProductReviewId);
    }

    [Fact]
    public async Task GetSellerReviews_AllowAnonymous_ReturnsOk()
    {
        var review = await CreateSellerReviewAsync(approved: true);

        Client.DefaultRequestHeaders.Authorization = null;
        var response = await Client.GetAsync($"/api/v1/reviews/seller/{review.SellerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<BusinessLogic.DTOs.Review.SellerReviewDto>>();
        list.Should().NotBeNull();
        list!.Should().Contain(r => r.SellerReviewId == review.SellerReviewId);
    }

    [Fact]
    public async Task GetPendingProductReviews_Admin_ReturnsOk()
    {
        var review = await CreateProductReviewAsync(approved: false);

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/reviews/product/pending");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<BusinessLogic.DTOs.Review.ProductReviewDto>>();
        list.Should().NotBeNull();
        list!.Should().Contain(r => r.ProductReviewId == review.ProductReviewId);
    }

    [Fact]
    public async Task GetPendingSellerReviews_Admin_ReturnsOk()
    {
        var review = await CreateSellerReviewAsync(approved: false);

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/reviews/seller/pending");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<BusinessLogic.DTOs.Review.SellerReviewDto>>();
        list.Should().NotBeNull();
        list!.Should().Contain(r => r.SellerReviewId == review.SellerReviewId);
    }
}
