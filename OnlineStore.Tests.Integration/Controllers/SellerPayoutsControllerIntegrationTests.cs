using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Payout;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class SellerPayoutsControllerIntegrationTests : ControllerIntegrationTestBase
{
    public SellerPayoutsControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

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

    private async Task<SellerPayout> CreatePendingPayoutAsync()
    {
        var db = GetService<AppDbContext>();
        var seller = await db.Seller.FirstOrDefaultAsync();
        if (seller is null)
        {
            var user = new User
            {
                PhoneNumber = $"0912{Random.Shared.Next(1000000, 9999999):D7}",
                FirstName = "Seller",
                LastName = "PayoutOwner",
                UserType = UserType.Customer,
                PasswordHash = "hash",
                SecurityStamp = Guid.NewGuid().ToString()
            };
            db.User.Add(user);
            await db.SaveChangesAsync();

            seller = new Seller
            {
                UserId = user.UserId,
                StoreName = $"PayoutStore-{Guid.NewGuid():N}",
                Status = SellerStatus.Approved,
                IsActive = true
            };
            db.Seller.Add(seller);
            await db.SaveChangesAsync();
        }

        var payout = new SellerPayout(
            seller.SellerId,
            null,
            250000m,
            DateTime.UtcNow.AddDays(-14),
            DateTime.UtcNow.AddDays(-7));

        db.SellerPayout.Add(payout);
        await db.SaveChangesAsync();

        return payout;
    }

    [Fact]
    public async Task Pay_Admin_MarksPayoutAsPaidAndReturnsOk()
    {
        var adminToken = await GetAdminTokenAsync();
        var payout = await CreatePendingPayoutAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var request = new MarkPayoutPaidRequest { Reference = "BANK-REF-9988" };
        var response = await Client.PostAsJsonAsync($"/api/v1/seller-payouts/{payout.SellerPayoutId}/pay", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SellerPayoutDto>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("Paid");
        result.Reference.Should().Be("BANK-REF-9988");
        result.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Pay_NonAdmin_ReturnsForbidden()
    {
        var (token, _) = await CreateCustomerAndTokenAsync();
        var payout = await CreatePendingPayoutAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new MarkPayoutPaidRequest { Reference = "UNAUTHORIZED-REF" };
        var response = await Client.PostAsJsonAsync($"/api/v1/seller-payouts/{payout.SellerPayoutId}/pay", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cancel_Admin_CancelsPayoutAndReturnsOk()
    {
        var adminToken = await GetAdminTokenAsync();
        var payout = await CreatePendingPayoutAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await Client.PostAsync($"/api/v1/seller-payouts/{payout.SellerPayoutId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SellerPayoutDto>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Cancel_NonAdmin_ReturnsForbidden()
    {
        var (token, _) = await CreateCustomerAndTokenAsync();
        var payout = await CreatePendingPayoutAsync();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PostAsync($"/api/v1/seller-payouts/{payout.SellerPayoutId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pay_NotFound_ReturnsNotFound()
    {
        var adminToken = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await Client.PostAsJsonAsync("/api/v1/seller-payouts/999999/pay", new MarkPayoutPaidRequest { Reference = "NONE" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
