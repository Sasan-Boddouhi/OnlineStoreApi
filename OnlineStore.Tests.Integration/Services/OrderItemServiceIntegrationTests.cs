using Application.Entities;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class OrderItemServiceIntegrationTests : BaseIntegrationTest
{
    private IOrderItemService OrderItemService => GetService<IOrderItemService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public OrderItemServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

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
    public async Task AddOrderItemAsync_ValidOrder_AddsItemAndUpdatesTotal()
    {
        // Arrange: مشتری و سفارش
        var customerId = await CreateCustomerAsync();
        var order = new Order
        {
            CustomerId = customerId,
            OrderDate = DateTime.UtcNow,
            ShippingFullName = "Test",
            ShippingAddress = "Addr",
            ShippingPhoneNumber = "0912"
        };
        typeof(Order).GetProperty("Status")?.SetValue(order, OrderStatus.Pending);
        typeof(Order).GetProperty("TotalAmount")?.SetValue(order, 0m);
        DbContext.Order.Add(order);
        await DbContext.SaveChangesAsync();

        // یک محصول برای استفاده در آیتم
        var product = new Product { Name = "TestProduct", Price = 100, SubcategoryId = 1 };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var dto = new CreateOrderItemDto
        {
            ProductId = product.ProductId,
            Quantity = 3,
            UnitPrice = product.Price,
            Description = "Three items"
        };

        // Act
        var result = await OrderItemService.AddOrderItemAsync(order.OrderId, dto);

        // Assert
        result.Should().NotBeNull();
        result.OrderItemId.Should().BeGreaterThan(0);
        result.Quantity.Should().Be(3);

        // بررسی به‌روز شدن مبلغ کل سفارش
        var updatedOrder = await DbContext.Order.FindAsync(order.OrderId);
        updatedOrder!.TotalAmount.Should().Be(300); // 3 * 100
    }
}