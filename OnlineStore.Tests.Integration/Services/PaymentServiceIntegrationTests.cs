using System.Reflection;
using Application.Entities;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class PaymentServiceIntegrationTests : BaseIntegrationTest
{
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public PaymentServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<int> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "Cust",
            LastName = "Test",
            PhoneNumber = $"0912{new Random().Next(1000000, 9999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();

        return customer.CustomerId;
    }

    [Fact]
    public async Task RecordPaymentAsync_ValidInvoice_RecordsPayment()
    {
        // 1. Create Customer
        var customerId = await CreateCustomerAsync();

        // 2. Create Order
        var order = new Order
        {
            CustomerId = customerId,
            OrderDate = DateTime.UtcNow,
            ShippingFullName = "Test",
            ShippingAddress = "Addr",
            ShippingPhoneNumber = "0912"
        };
        typeof(Order).GetProperty("Status")?.SetValue(order, OrderStatus.Processing);
        DbContext.Order.Add(order);
        await DbContext.SaveChangesAsync();

        // 3. Create Invoice connected to Order
        var invoice = new Invoice
        {
            OrderId = order.OrderId,
            InvoiceNumber = "INV-PMT-INT"
        };
        typeof(Invoice).GetProperty("Status")?.SetValue(invoice, InvoiceStatus.Paid);
        DbContext.Invoice.Add(invoice);
        await DbContext.SaveChangesAsync();

        // 4. Record Payment
        var result = await PaymentService.RecordPaymentAsync(invoice.InvoiceId, 300, "TRX-INT-001");
        result.Should().NotBeNull();
        result.Amount.Should().Be(300);
    }
}