using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Payment;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class PaymentsControllerIntegrationTests : ControllerIntegrationTestBase
{
    public PaymentsControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<(Order Order, Payment Payment)> SeedOrderAndPaymentAsync()
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
            ShippingFullName = "Payment Test",
            ShippingAddress = "Street 1",
            ShippingPhoneNumber = "09120000000"
        };
        order.Confirm();
        db.Order.Add(order);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            OrderId = order.OrderId,
            InvoiceNumber = "INV-" + Guid.NewGuid().ToString("N")[..8],
            SubTotalAmount = 100,
            TotalAmount = 100
        };
        db.Invoice.Add(invoice);
        await db.SaveChangesAsync();

        var payment = new Payment
        {
            OrderId = order.OrderId,
            InvoiceId = invoice.InvoiceId,
            Amount = 100,
            PaymentMethod = PaymentMethod.CreditCard,
            Gateway = PaymentGatewayType.Manual,
            State = PaymentState.Initiated,
            TransactionId = "AUTH-" + Guid.NewGuid().ToString("N")[..8]
        };
        payment.TransitionTo(PaymentState.Pending);
        payment.TransitionTo(PaymentState.Succeeded);
        db.Payment.Add(payment);
        await db.SaveChangesAsync();

        return (order, payment);
    }

    [Fact]
    public async Task GetByOrder_Admin_ReturnsOk()
    {
        var (order, _) = await SeedOrderAndPaymentAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/v1/payments/order/{order.OrderId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetByOrder_OtherCustomer_ReturnsForbidden()
    {
        var (order, _) = await SeedOrderAndPaymentAsync();
        var phone = $"0912{Random.Shared.Next(0, 10_000_000):D7}";
        var registerData = new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "Other",
            LastName = "Customer",
            DeviceId = "device-other-customer",
            DateOfBirth = "1370/01/01"
        };
        var regRes = await Client.PostAsJsonAsync("/api/v1/auth/register", registerData);
        regRes.EnsureSuccessStatusCode();
        var auth = await regRes.Content.ReadFromJsonAsync<BusinessLogic.DTOs.Auth.AuthResultDto>();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var response = await Client.GetAsync($"/api/v1/payments/order/{order.OrderId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Initiate_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var body = new InitiatePaymentRequest
        {
            OrderId = 1
        };

        var response = await Client.PostAsJsonAsync("/api/v1/payments/initiate", body);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refund_Admin_ReturnsOk()
    {
        var (_, payment) = await SeedOrderAndPaymentAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new RefundPaymentRequest
        {
            Reason = "Customer requested return"
        };

        var response = await Client.PostAsJsonAsync($"/api/v1/payments/{payment.PaymentId}/refund", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
