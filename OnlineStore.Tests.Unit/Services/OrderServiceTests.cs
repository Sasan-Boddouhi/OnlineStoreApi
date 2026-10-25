using System.Linq.Expressions;
using System.Reflection;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Application.Common.Specifications;
using Application.Options;
using Microsoft.Extensions.Options;

namespace OnlineStore.Tests.Unit.Services;

public class OrderServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<OrderService>> _loggerMock;
    private Mock<IAuditService> _auditMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IInventoryService> _inventoryMock;
    private readonly OrderService _service;

    private readonly Mock<IGenericRepository<Order>> _orderRepoMock;
    private readonly Mock<IGenericRepository<Customer>> _customerRepoMock;
    private readonly Mock<IGenericRepository<SellerProduct>> _listingRepoMock;
    private readonly Mock<IGenericRepository<Seller>> _sellerRepoMock;
    private readonly Mock<IGenericRepository<Payment>> _paymentRepoMock;

    public OrderServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<OrderService>>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _inventoryMock = new Mock<IInventoryService>();

        _orderRepoMock = new Mock<IGenericRepository<Order>>();
        _customerRepoMock = new Mock<IGenericRepository<Customer>>();
        _listingRepoMock = new Mock<IGenericRepository<SellerProduct>>();
        _sellerRepoMock = new Mock<IGenericRepository<Seller>>();

        _uowMock.Setup(u => u.Repository<Order>()).Returns(_orderRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Customer>()).Returns(_customerRepoMock.Object);
        _uowMock.Setup(u => u.Repository<SellerProduct>()).Returns(_listingRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Seller>()).Returns(_sellerRepoMock.Object);

        // Phase 8: لغو سفارش، پرداخت‌های موفق را Refunded می‌کند.
        _paymentRepoMock = new Mock<IGenericRepository<Payment>>();
        _paymentRepoMock
            .Setup(r => r.ListAsync(It.IsAny<Spec<Payment>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
        _uowMock.Setup(u => u.Repository<Payment>()).Returns(_paymentRepoMock.Object);

        // Phase 9 (R40): لغو سفارش حسابرسی می‌شود.
        _auditMock = new Mock<IAuditService>();
        _auditMock
            .Setup(a => a.RecordAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new OrderService(
            _uowMock.Object,
            _mapperMock.Object,
            _loggerMock.Object,
            _currentUserMock.Object,
            _inventoryMock.Object,
            _auditMock.Object,
            Options.Create(new CommissionSettings { DefaultRate = 10m }));
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

    /// <summary>کاربر جاری = UserId 10 که CustomerId آن 1 است (مالک CreateValidOrder).</summary>
    private void SetupOwnedCustomer()
    {
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(10);
        _customerRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { CustomerId = 1, UserId = 10 });
    }

    /// <summary>کاربر جاری وجود دارد اما سفارش متعلق به دیگری است.</summary>
    private void SetupForeignOrder()
    {
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(10);
        _customerRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { CustomerId = 2, UserId = 10 });
        _orderRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);
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
        // Phase 2: قیمت و موجودی از Listing می‌آید، نه از Product.
        var listing = new SellerProduct
        {
            ListingId = 20,
            SellerId = 1,
            VariantId = 10,
            Price = 100,
            Status = SellerProductStatus.Active
        };

        SetupOwnedCustomer();
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _listingRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<SellerProduct>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(listing);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<OrderDto>(order)).Returns(new OrderDto { OrderId = 1, TotalAmount = 200 });

        var itemDto = new AddOrderItemDto { ListingId = 20, Quantity = 2, Description = "Test" };
        var result = await _service.AddItemToOrderAsync(1, itemDto);

        result.Should().NotBeNull();
        result.TotalAmount.Should().Be(200);
        _orderRepoMock.Verify(r => r.Update(order), Times.Once);
        _inventoryMock.Verify(
            i => i.ReserveAsync(20, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddItemToOrderAsync_OrderNotFound_ThrowsNotFoundException()
    {
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(10);
        _customerRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { CustomerId = 1, UserId = 10 });
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.AddItemToOrderAsync(999, new AddOrderItemDto()));
    }

    [Fact]
    public async Task AddItemToOrderAsync_CustomerProfileMissing_ThrowsNotFoundException()
    {
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(10);
        _customerRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.AddItemToOrderAsync(1, new AddOrderItemDto { ListingId = 10, Quantity = 1 }));
    }

    [Fact]
    public async Task AddItemToOrderAsync_NonOwnedOrder_ThrowsNotFoundException()
    {
        SetupForeignOrder();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.AddItemToOrderAsync(1, new AddOrderItemDto { ListingId = 10, Quantity = 1 }));

        _inventoryMock.Verify(
            i => i.ReserveAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddItemToOrderAsync_OrderNotPending_ThrowsBusinessException()
    {
        var order = CreateValidOrder(1, status: OrderStatus.Cancelled);
        SetupOwnedCustomer();
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
        var item = new OrderItem(10, 1, 50, "desc") { OrderItemId = 100, ListingId = 10 };
        order.AddItem(item);

        SetupOwnedCustomer();
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<OrderDto>(order)).Returns(new OrderDto { OrderId = 1 });

        var result = await _service.RemoveItemFromOrderAsync(1, 100);
        result.Should().NotBeNull();
        _orderRepoMock.Verify(r => r.Update(order), Times.Once);
        _inventoryMock.Verify(
            i => i.ReleaseAsync(10, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveItemFromOrderAsync_ItemNotFound_ThrowsBusinessException()
    {
        var order = CreateValidOrder(1);
        SetupOwnedCustomer();
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.RemoveItemFromOrderAsync(1, 999));
    }

    [Fact]
    public async Task RemoveItemFromOrderAsync_NonOwnedOrder_ThrowsNotFoundException()
    {
        SetupForeignOrder();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.RemoveItemFromOrderAsync(1, 100));

        _inventoryMock.Verify(
            i => i.ReleaseAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------- ConfirmOrderAsync ---------------
    [Fact]
    public async Task ConfirmOrderAsync_ValidOrder_ConfirmsAndReturnsDto()
    {
        var order = CreateValidOrder(1, status: OrderStatus.Pending);
        SetupOwnedCustomer();
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<OrderDto>(order)).Returns(new OrderDto { OrderId = 1, Status = "Confirmed" });

        var result = await _service.ConfirmOrderAsync(1);
        result.Status.Should().Be("Confirmed");
        order.Status.Should().NotBe(OrderStatus.Pending);
    }

    [Fact]
    public async Task ConfirmOrderAsync_OrderNotFound_ThrowsNotFoundException()
    {
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(10);
        _customerRepoMock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { CustomerId = 1, UserId = 10 });
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ConfirmOrderAsync(1));
    }

    [Fact]
    public async Task ConfirmOrderAsync_NonOwnedOrder_ThrowsNotFoundException()
    {
        SetupForeignOrder();

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ConfirmOrderAsync(1));
    }

    // ------------- CancelOrderAsync ---------------
    [Fact]
    public async Task CancelOrderAsync_ValidOrder_CancelsAndReturnsDto()
    {
        var order = CreateValidOrder(1, status: OrderStatus.Pending);
        SetupOwnedCustomer();
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<OrderDto>(order)).Returns(new OrderDto { OrderId = 1, Status = "Cancelled" });

        var result = await _service.CancelOrderAsync(1);
        result.Status.Should().Be("Cancelled");
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task CancelOrderAsync_NonOwnedOrder_ThrowsNotFoundException()
    {
        SetupForeignOrder();

        await Assert.ThrowsAsync<NotFoundException>(() => _service.CancelOrderAsync(1));

        _inventoryMock.Verify(
            i => i.ReleaseAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ------------- GetOrdersAsync ---------------
    [Fact]
    public async Task GetOrdersAsync_ReturnsMappedDtos()
    {
        var orders = new List<Order> { CreateValidOrder(1), CreateValidOrder(2) };
        _customerRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { CustomerId = 1, UserId = 10 });
        _orderRepoMock.Setup(r => r.ListAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);
        _mapperMock.Setup(m => m.Map<IEnumerable<OrderDto>>(orders))
            .Returns(orders.Select(o => new OrderDto { OrderId = o.OrderId }));

        var result = await _service.GetOrdersAsync(10);
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

        _customerRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { CustomerId = 1, UserId = 10 });
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var result = await _service.GetOrderDetailsAsync(10, 1);
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.InvoiceNumber.Should().Be("INV-001");
        result.IsPaid.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrderDetailsAsync_OrderNotFound_ReturnsNull()
    {
        _customerRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { CustomerId = 1, UserId = 10 });
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);
        var result = await _service.GetOrderDetailsAsync(10, 1);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrderDetailsAsync_NonOwnedOrder_ReturnsNull()
    {
        _customerRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { CustomerId = 2, UserId = 20 });
        _orderRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Order>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var result = await _service.GetOrderDetailsAsync(10, 2);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrdersAsync_NonOwnedUser_ReturnsEmpty()
    {
        _customerRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<Customer>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var result = await _service.GetOrdersAsync(999);

        result.Should().BeEmpty();
    }
}
