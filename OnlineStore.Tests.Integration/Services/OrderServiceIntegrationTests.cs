using Application.Entities;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class OrderServiceIntegrationTests : BaseIntegrationTest
{
    private IOrderService OrderService => GetService<IOrderService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public OrderServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<(Customer customer, User user)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "Cust",
            LastName = "Omer",
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

        return (customer, user);
    }

    [Fact]
    public async Task CreateEmptyOrderAsync_ValidCustomer_CreatesOrder()
    {
        var (customer, _) = await CreateCustomerAsync();
        var result = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Ali", "Addr", "09121111111");
        result.Should().NotBeNull();
        result.OrderId.Should().BeGreaterThan(0);

        var fromDb = await DbContext.Order.FindAsync(result.OrderId);
        fromDb.Should().NotBeNull();
        fromDb!.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task AddItemToOrderAsync_ValidProduct_AddsItem()
    {
        var (customer, _) = await CreateCustomerAsync();
        var orderDto = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        // رفع مشکل tracking با جدا کردن موقت موجودیت
        var orderEntity = await DbContext.Order.FindAsync(orderDto.OrderId);
        if (orderEntity != null)
            DbContext.Entry(orderEntity).State = EntityState.Detached;

        var product = new Product { Name = "Test Product", Price = 50, SubcategoryId = 1 };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var itemDto = new AddOrderItemDto { ProductId = product.ProductId, Quantity = 2, Description = "Item" };
        var updated = await OrderService.AddItemToOrderAsync(orderDto.OrderId, itemDto);
        updated.TotalAmount.Should().Be(100);

        var items = await DbContext.OrderItem.Where(i => i.OrderId == orderDto.OrderId).ToListAsync();
        items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConfirmOrderAsync_ChangesStatus()
    {
        var (customer, _) = await CreateCustomerAsync();
        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var confirmed = await OrderService.ConfirmOrderAsync(order.OrderId);
        confirmed.Status.Should().NotBe("Pending");

        var fromDb = await DbContext.Order.FindAsync(order.OrderId);
        fromDb!.Status.Should().NotBe(OrderStatus.Pending);
    }

    [Fact]
    public async Task CancelOrderAsync_ChangesStatus()
    {
        var (customer, _) = await CreateCustomerAsync();
        var order = await OrderService.CreateEmptyOrderAsync(customer.CustomerId, "Test", "Addr", "09120000000");

        var cancelled = await OrderService.CancelOrderAsync(order.OrderId);
        cancelled.Status.Should().Be("Cancelled");

        var fromDb = await DbContext.Order.FindAsync(order.OrderId);
        fromDb!.Status.Should().Be(OrderStatus.Cancelled);
    }
}