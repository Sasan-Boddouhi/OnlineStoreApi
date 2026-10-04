using Application.Entities;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessLogic.DTOs.Order;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class OrdersControllerIntegrationTests : ControllerIntegrationTestBase
{
    public OrdersControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task GetOrders_Authenticated_ReturnsOk()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var orders = await response.Content.ReadFromJsonAsync<List<OrderDto>>();
        orders.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrders_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/v1/orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOrderById_ExistingOwnedOrder_ReturnsOk()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var db = GetService<AppDbContext>();
        var admin = await db.User.FirstAsync(u => u.PhoneNumber == "09123456789");

        var customer = await db.Customer.FirstOrDefaultAsync(c => c.UserId == admin.UserId);
        if (customer is null)
        {
            customer = new Customer { UserId = admin.UserId };
            db.Customer.Add(customer);
            await db.SaveChangesAsync();
        }

        var order = new Order
        {
            CustomerId = customer.CustomerId,
            ShippingFullName = "Integration Test",
            ShippingAddress = "Test Address",
            ShippingPhoneNumber = "09120000000"
        };
        db.Order.Add(order);
        await db.SaveChangesAsync();

        var response = await Client.GetAsync($"/api/v1/orders/{order.OrderId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var details = await response.Content.ReadFromJsonAsync<OrderDetailsDto>();
        details.Should().NotBeNull();
        details!.OrderId.Should().Be(order.OrderId);
    }

    [Fact]
    public async Task GetOrderById_NonOwnedOrder_ReturnsNotFound()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/orders/2");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetOrderById_NonExistingOrder_ReturnsNotFound()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/orders/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}