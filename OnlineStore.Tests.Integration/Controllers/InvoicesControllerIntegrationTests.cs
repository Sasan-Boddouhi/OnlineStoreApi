using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Invoice;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Online_Store_Application.Controllers;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class InvoicesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public InvoicesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<(Order Order, Invoice Invoice)> SeedOrderAndInvoiceAsync()
    {
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
            ShippingFullName = "Test Invoice",
            ShippingAddress = "Address",
            ShippingPhoneNumber = "09120000000"
        };
        order.Confirm();
        db.Order.Add(order);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            OrderId = order.OrderId,
            InvoiceNumber = "INV-" + Guid.NewGuid().ToString("N")[..8],
            SubTotalAmount = 500,
            TotalAmount = 500
        };
        db.Invoice.Add(invoice);
        await db.SaveChangesAsync();

        return (order, invoice);
    }

    [Fact]
    public async Task GetById_Admin_ReturnsOk()
    {
        var (_, invoice) = await SeedOrderAndInvoiceAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/v1/invoices/{invoice.InvoiceId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetByOrder_Admin_ReturnsOk()
    {
        var (order, _) = await SeedOrderAndInvoiceAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/v1/invoices/by-order/{order.OrderId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMy_Admin_ReturnsOk()
    {
        await SeedOrderAndInvoiceAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/invoices/my");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MarkAsPaid_Admin_ReturnsOk()
    {
        var (_, invoice) = await SeedOrderAndInvoiceAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PostAsync($"/api/v1/invoices/{invoice.InvoiceId}/pay", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync("/api/v1/invoices/1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
