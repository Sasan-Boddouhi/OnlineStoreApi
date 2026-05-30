using System.Linq.Expressions;
using System.Reflection;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Application.Common.Specifications;
using Application.DTOs.Order;

namespace OnlineStore.Tests.Unit.Services;

public class OrderServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<OrderService>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly OrderService _service;

    private readonly Mock<IGenericRepository<Order>> _orderRepoMock;
    private readonly Mock<IGenericRepository<Customer>> _customerRepoMock;
    private readonly Mock<IGenericRepository<Product>> _productRepoMock;

    public OrderServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<OrderService>>();
        _currentUserMock = new Mock<ICurrentUserService>();

        _orderRepoMock = new Mock<IGenericRepository<Order>>();
        _customerRepoMock = new Mock<IGenericRepository<Customer>>();
        _productRepoMock = new Mock<IGenericRepository<Product>>();

        _uowMock.Setup(u => u.Repository<Order>()).Returns(_orderRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Customer>()).Returns(_customerRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Product>()).Returns(_productRepoMock.Object);

        _service = new OrderService(
            _uowMock.Object,
            _mapperMock.Object,
            _loggerMock.Object,
            _currentUserMock.Object);
    }

    // ---------- Helpers ----------
    private static Order CreateValidOrder(int id, OrderStatus status = OrderStatus.Pending)
    {
        var order = new Order
        {
            OrderId = id,
            CustomerId = 1,
            OrderDate = DateTime.UtcNow,
            ShippingFullName = "Test",
            ShippingAddress = "Test",
            ShippingPhoneNumber = "09121111111"
        };
        // تنظیم Status از طریق Reflection (چون set خصوصی است)
        typeof(Order).GetProperty("Status")?.SetValue(order, status);
        return order;
    }

    private static Invoice CreateInvoiceWithStatus(InvoiceStatus status)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = "INV-001"
        };
        typeof(Invoice).GetProperty("Status")?.SetValue(invoice, status);
        return invoice;
    }

    // ------------- CreateEmptyOrderAsync ---------------
    [Fact]
    public async Task CreateEmptyOrderAsync_ValidCustomer_ReturnsOrderDto()
    {
        var customer = new Customer { CustomerId = 1, UserId = 10 };
        _customerRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _orderRepoMock.Setup(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<OrderDto>(It.IsAny<Order>())).Returns(new OrderDto { OrderId = 100 });

        var result = await _service.CreateEmptyOrderAsync(1, "Ali", "Addr", "09121111111");
        result.Should().NotBeNull();
        result.OrderId.Should().Be(100);
    }

    [Fact]
    public async Task CreateEmptyOrderAsync_CustomerNotFound_ThrowsBusinessException()
    {
        _customerRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Customer?)null);
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.CreateEmptyOrderAsync(1, "Ali", "Addr", "09121111111"));
    }

    // ------------- AddItemToOrderAsync ---------------
    [Fact]
    public async Task AddItemToOrderAsync_ValidOrderAndProduct_AddsItemAndReturnsDto()
    {
        var order = CreateValidOrder(1, status: OrderStatus.Pending);
        var product = new Product { ProductId = 10, Name = "P1", Price = 100, SubcategoryId = 1 };

        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _productRepoMock.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<OrderDto>(order)).Returns(new OrderDto { OrderId = 1, TotalAmount = 200 });

        var itemDto = new AddOrderItemDto { ProductId = 10, Quantity = 2, Description = "Test" };
        var result = await _service.AddItemToOrderAsync(1, itemDto);

        result.Should().NotBeNull();
        result.TotalAmount.Should().Be(200);
        _orderRepoMock.Verify(r => r.Update(order), Times.Once);
    }

    [Fact]
    public async Task AddItemToOrderAsync_OrderNotFound_ThrowsBusinessException()
    {
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AddItemToOrderAsync(999, new AddOrderItemDto()));
    }

    [Fact]
    public async Task AddItemToOrderAsync_OrderNotPending_ThrowsBusinessException()
    {
        var order = CreateValidOrder(1, status: OrderStatus.Cancelled);
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.AddItemToOrderAsync(1, new AddOrderItemDto()));
    }

    // ------------- RemoveItemFromOrderAsync ---------------
    [Fact]
    public async Task RemoveItemFromOrderAsync_ValidItem_RemovesAndReturnsDto()
    {
        var order = CreateValidOrder(1);
        var item = new OrderItem(10, 1, 50, "desc") { OrderItemId = 100 };
        order.AddItem(item);

        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<OrderDto>(order)).Returns(new OrderDto { OrderId = 1 });

        var result = await _service.RemoveItemFromOrderAsync(1, 100);
        result.Should().NotBeNull();
        _orderRepoMock.Verify(r => r.Update(order), Times.Once);
    }

    [Fact]
    public async Task RemoveItemFromOrderAsync_ItemNotFound_ThrowsBusinessException()
    {
        var order = CreateValidOrder(1);
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.RemoveItemFromOrderAsync(1, 999));
    }

    // ------------- ConfirmOrderAsync ---------------
    [Fact]
    public async Task ConfirmOrderAsync_ValidOrder_ConfirmsAndReturnsDto()
    {
        var order = CreateValidOrder(1, status: OrderStatus.Pending);
        _orderRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        // برای خروجی از یک DTO با وضعیت ثابت استفاده می‌کنیم تا به تغییرات واقعی order وابسته نباشیم
        _mapperMock.Setup(m => m.Map<OrderDto>(order)).Returns(new OrderDto { OrderId = 1, Status = "Confirmed" });

        var result = await _service.ConfirmOrderAsync(1);
        result.Status.Should().Be("Confirmed");
        order.Status.Should().NotBe(OrderStatus.Pending);
    }

    [Fact]
    public async Task ConfirmOrderAsync_OrderNotFound_ThrowsBusinessException()
    {
        _orderRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);
        await Assert.ThrowsAsync<BusinessException>(() => _service.ConfirmOrderAsync(1));
    }

    // ------------- CancelOrderAsync ---------------
    [Fact]
    public async Task CancelOrderAsync_ValidOrder_CancelsAndReturnsDto()
    {
        var order = CreateValidOrder(1, status: OrderStatus.Pending);
        _orderRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<OrderDto>(order)).Returns(new OrderDto { OrderId = 1, Status = "Cancelled" });

        var result = await _service.CancelOrderAsync(1);
        result.Status.Should().Be("Cancelled");
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    // ------------- GetOrdersAsync ---------------
    [Fact]
    public async Task GetOrdersAsync_ReturnsMappedDtos()
    {
        var orders = new List<Order> { CreateValidOrder(1), CreateValidOrder(2) };
        _orderRepoMock.Setup(r => r.ListAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);
        _mapperMock.Setup(m => m.Map<IEnumerable<OrderDto>>(orders))
            .Returns(orders.Select(o => new OrderDto { OrderId = o.OrderId }));

        var result = await _service.GetOrdersAsync(1);
        result.Should().HaveCount(2);
    }

    // ------------- GetOrderDetailsAsync ---------------
    [Fact]
    public async Task GetOrderDetailsAsync_OrderExists_ReturnsDetails()
    {
        var order = CreateValidOrder(1);
        var item = new OrderItem(10, 2, 50, "desc") { OrderItemId = 5 };
        order.AddItem(item);
        order.Invoice = CreateInvoiceWithStatus(InvoiceStatus.Paid);

        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var result = await _service.GetOrderDetailsAsync(1);
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.InvoiceNumber.Should().Be("INV-001");
        result.IsPaid.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrderDetailsAsync_OrderNotFound_ReturnsNull()
    {
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);
        var result = await _service.GetOrderDetailsAsync(1);
        result.Should().BeNull();
    }
}