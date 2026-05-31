using System.Linq.Expressions;
using System.Reflection;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace OnlineStore.Tests.Unit.Services;

public class OrderItemServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<OrderItemService>> _loggerMock;
    private readonly OrderItemService _service;

    private readonly Mock<IGenericRepository<Order>> _orderRepoMock;
    private readonly Mock<IGenericRepository<OrderItem>> _orderItemRepoMock;

    public OrderItemServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<OrderItemService>>();

        _orderRepoMock = new Mock<IGenericRepository<Order>>();
        _orderItemRepoMock = new Mock<IGenericRepository<OrderItem>>();

        _uowMock.Setup(u => u.Repository<Order>()).Returns(_orderRepoMock.Object);
        _uowMock.Setup(u => u.Repository<OrderItem>()).Returns(_orderItemRepoMock.Object);

        _service = new OrderItemService(_uowMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    private static Order CreateValidOrder(int id, OrderStatus status = OrderStatus.Pending)
    {
        var order = new Order
        {
            OrderId = id,
            CustomerId = 1,
            OrderDate = DateTime.UtcNow,
            ShippingFullName = "Test",
            ShippingAddress = "Addr",
            ShippingPhoneNumber = "0912"
        };
        typeof(Order).GetProperty("Status")?.SetValue(order, status);
        typeof(Order).GetProperty("TotalAmount")?.SetValue(order, 0m);
        return order;
    }

    [Fact]
    public async Task AddOrderItemAsync_ValidOrder_AddsItemAndReturnsDto()
    {
        // Arrange
        var order = CreateValidOrder(1);
        var dto = new CreateOrderItemDto
        {
            ProductId = 10,
            Quantity = 2,
            UnitPrice = 100,
            Description = "Test item"
        };

        _orderRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _orderItemRepoMock.Setup(r => r.AddAsync(It.IsAny<OrderItem>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<OrderItemDto>(It.IsAny<OrderItem>())).Returns(new OrderItemDto { OrderItemId = 5, Quantity = 2 });

        // Act
        var result = await _service.AddOrderItemAsync(1, dto);

        // Assert
        result.Should().NotBeNull();
        result.OrderItemId.Should().Be(5);
        result.Quantity.Should().Be(2);
        _orderItemRepoMock.Verify(r => r.AddAsync(It.IsAny<OrderItem>(), It.IsAny<CancellationToken>()), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddOrderItemAsync_OrderNotFound_ThrowsBusinessException()
    {
        _orderRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

        Func<Task> act = () => _service.AddOrderItemAsync(1, new CreateOrderItemDto());
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*سفارش*");
    }
}