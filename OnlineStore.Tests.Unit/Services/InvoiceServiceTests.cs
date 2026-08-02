using System.Reflection;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Invoice;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace OnlineStore.Tests.Unit.Services;

public class InvoiceServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<InvoiceService>> _loggerMock;
    private readonly InvoiceService _service;

    private readonly Mock<IGenericRepository<Order>> _orderRepoMock;
    private readonly Mock<IGenericRepository<Invoice>> _invoiceRepoMock;

    public InvoiceServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<InvoiceService>>();

        _orderRepoMock = new Mock<IGenericRepository<Order>>();
        _invoiceRepoMock = new Mock<IGenericRepository<Invoice>>();

        _uowMock.Setup(u => u.Repository<Order>()).Returns(_orderRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Invoice>()).Returns(_invoiceRepoMock.Object);

        _service = new InvoiceService(_uowMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    private static Order CreateOrder(int id, OrderStatus status, decimal totalAmount, Invoice? invoice = null)
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
        typeof(Order).GetProperty("TotalAmount")?.SetValue(order, totalAmount);
        if (invoice != null)
            typeof(Order).GetProperty("Invoice")?.SetValue(order, invoice);
        return order;
    }

    [Fact]
    public async Task CreateInvoiceAsync_ValidOrder_CreatesInvoice()
    {
        var order = CreateOrder(1, OrderStatus.Processing, 1000);
        _orderRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        _invoiceRepoMock.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var expectedDto = new InvoiceDto { InvoiceId = 10, InvoiceNumber = "INV-test" };
        _mapperMock.Setup(m => m.Map<InvoiceDto>(It.IsAny<Invoice>())).Returns(expectedDto);

        var result = await _service.CreateInvoiceAsync(1, 0, 0);
        result.Should().NotBeNull();
        result.InvoiceId.Should().Be(10);
    }

    [Fact]
    public async Task CreateInvoiceAsync_OrderNotFound_ThrowsBusinessException()
    {
        _orderRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);
        Func<Task> act = () => _service.CreateInvoiceAsync(1);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*سفارش*");
    }

    [Fact]
    public async Task CreateInvoiceAsync_OrderNotProcessing_ThrowsBusinessException()
    {
        var order = CreateOrder(1, OrderStatus.Pending, 500);
        _orderRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        Func<Task> act = () => _service.CreateInvoiceAsync(1);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*تایید*");
    }

    [Fact]
    public async Task CreateInvoiceAsync_InvoiceAlreadyExists_ThrowsBusinessException()
    {
        var existingInvoice = new Invoice();
        var order = CreateOrder(1, OrderStatus.Processing, 500, existingInvoice);
        _orderRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        Func<Task> act = () => _service.CreateInvoiceAsync(1);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("فاکتور قبلاً ایجاد شده است.");
    }
}