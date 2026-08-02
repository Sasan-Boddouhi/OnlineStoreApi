using System.Linq.Expressions;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace OnlineStore.Tests.Unit.Services;

public class PaymentServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<PaymentService>> _loggerMock;
    private readonly PaymentService _service;

    private readonly Mock<IGenericRepository<Invoice>> _invoiceRepoMock;
    private readonly Mock<IGenericRepository<Payment>> _paymentRepoMock;

    public PaymentServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<PaymentService>>();

        _invoiceRepoMock = new Mock<IGenericRepository<Invoice>>();
        _paymentRepoMock = new Mock<IGenericRepository<Payment>>();

        _uowMock.Setup(u => u.Repository<Invoice>()).Returns(_invoiceRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Payment>()).Returns(_paymentRepoMock.Object);

        _service = new PaymentService(_uowMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task RecordPaymentAsync_ValidInvoice_ReturnsPaymentDto()
    {
        // Arrange
        var invoice = new Invoice { InvoiceId = 1, InvoiceNumber = "INV-001" };
        typeof(Invoice).GetProperty("Status")?.SetValue(invoice, InvoiceStatus.Paid); // پس از MarkAsPaid

        _invoiceRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        _paymentRepoMock.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mapperMock.Setup(m => m.Map<PaymentDto>(It.IsAny<Payment>())).Returns(new PaymentDto { PaymentId = 10, Amount = 500 });

        // Act
        var result = await _service.RecordPaymentAsync(1, 500, "TRX-001");

        // Assert
        result.Should().NotBeNull();
        result.PaymentId.Should().Be(10);
        result.Amount.Should().Be(500);
    }

    [Fact]
    public async Task RecordPaymentAsync_InvoiceNotFound_ThrowsBusinessException()
    {
        _invoiceRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Invoice?)null);
        Func<Task> act = () => _service.RecordPaymentAsync(1, 100, "TRX");
        await act.Should().ThrowAsync<BusinessException>().WithMessage("فاکتور یافت نشد.");
    }
}