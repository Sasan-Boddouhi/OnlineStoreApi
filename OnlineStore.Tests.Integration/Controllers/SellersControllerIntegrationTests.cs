using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Seller;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class SellersControllerIntegrationTests : ControllerIntegrationTestBase
{
    public SellersControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<(string Token, int UserId)> CreateCustomerUserAndTokenAsync()
    {
        var phone = $"0912{Random.Shared.Next(1000000, 9999999):D7}";
        var registerData = new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "Cust",
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

    [Fact]
    public async Task Create_AuthenticatedCustomer_ReturnsCreated()
    {
        var (token, userId) = await CreateCustomerUserAndTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateSellerDto
        {
            StoreName = "Store-" + Guid.NewGuid().ToString("N")[..8],
            Description = "My store description"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/sellers", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var dto = new CreateSellerDto
        {
            StoreName = "UnauthStore"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/sellers", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_AllowAnonymous_ReturnsOk()
    {
        var (token, userId) = await CreateCustomerUserAndTokenAsync();
        var db = GetService<AppDbContext>();
        var seller = new Seller
        {
            UserId = userId,
            StoreName = "PubStore-" + Guid.NewGuid().ToString("N")[..8],
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = null;
        var response = await Client.GetAsync($"/api/v1/sellers/{seller.SellerId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMe_AuthenticatedSeller_ReturnsOk()
    {
        var (token, userId) = await CreateCustomerUserAndTokenAsync();
        var db = GetService<AppDbContext>();
        var seller = new Seller
        {
            UserId = userId,
            StoreName = "MeStore-" + Guid.NewGuid().ToString("N")[..8],
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.GetAsync("/api/v1/sellers/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAll_Admin_ReturnsOk()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/sellers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_Admin_ReturnsNoContent()
    {
        var (token, userId) = await CreateCustomerUserAndTokenAsync();
        var db = GetService<AppDbContext>();
        var seller = new Seller
        {
            UserId = userId,
            StoreName = "DelStore-" + Guid.NewGuid().ToString("N")[..8],
            Status = SellerStatus.Pending,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        var adminToken = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await Client.DeleteAsync($"/api/v1/sellers/{seller.SellerId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
