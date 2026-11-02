using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Dashboard;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class SellerDashboardControllerIntegrationTests : ControllerIntegrationTestBase
{
    public SellerDashboardControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<(string Token, int UserId, Seller Seller)> CreateSellerAndTokenAsync()
    {
        var phone = $"0912{Random.Shared.Next(1000000, 9999999):D7}";
        var registerData = new
        {
            PhoneNumber = phone,
            Password = "Seller@123",
            FirstName = "Seller",
            LastName = "Dash",
            DeviceId = $"device-{Guid.NewGuid():N}",
            DateOfBirth = "1370/01/01"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", registerData);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();
        var db = GetService<AppDbContext>();
        var user = await db.User.FirstAsync(u => u.PhoneNumber == phone);

        var seller = new Seller
        {
            UserId = user.UserId,
            StoreName = $"DashStore-{Guid.NewGuid():N}[..8]",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        db.Seller.Add(seller);
        await db.SaveChangesAsync();

        return (result!.AccessToken, user.UserId, seller);
    }

    [Fact]
    public async Task GetSummary_OwnerSeller_ReturnsOk()
    {
        var (token, _, seller) = await CreateSellerAndTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/v1/sellers/{seller.SellerId}/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTopProducts_OwnerSeller_ReturnsOk()
    {
        var (token, _, seller) = await CreateSellerAndTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/v1/sellers/{seller.SellerId}/dashboard/top-products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRecentOrders_OwnerSeller_ReturnsOk()
    {
        var (token, _, seller) = await CreateSellerAndTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/v1/sellers/{seller.SellerId}/dashboard/recent-orders");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSummary_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync("/api/v1/sellers/1/dashboard/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
