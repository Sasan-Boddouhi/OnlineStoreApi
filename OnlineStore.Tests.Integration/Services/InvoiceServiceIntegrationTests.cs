using System.Reflection;
using Application.Entities;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class InvoiceServiceIntegrationTests : BaseIntegrationTest
{
    private IInvoiceService InvoiceService => GetService<IInvoiceService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public InvoiceServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

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
    public async Task CreateInvoiceAsync_ValidOrder_CreatesInvoice()
    {
        // Arrange: ایجاد مشتری
        var customerId = await CreateCustomerAsync();

        var order = new Order
        {
            CustomerId = customerId,
            OrderDate = DateTime.UtcNow,
            ShippingFullName = "Test",
            ShippingAddress = "Test",
            ShippingPhoneNumber = "0912"
        };
        typeof(Order).GetProperty("Status")?.SetValue(order, OrderStatus.Processing);
        typeof(Order).GetProperty("TotalAmount")?.SetValue(order, 2000m);
        DbContext.Order.Add(order);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await InvoiceService.CreateInvoiceAsync(order.OrderId, 100, 50);
        result.Should().NotBeNull();
        result.InvoiceId.Should().BeGreaterThan(0);
        result.TotalAmount.Should().Be(2050);

        // Assert
        var invoiceInDb = await DbContext.Invoice.FindAsync(result.InvoiceId);
        invoiceInDb.Should().NotBeNull();
        invoiceInDb!.OrderId.Should().Be(order.OrderId);
    }
}