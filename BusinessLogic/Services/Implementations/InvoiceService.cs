using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Invoice;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    public sealed class InvoiceService : IInvoiceService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<InvoiceService> _logger;

        public InvoiceService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<InvoiceService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<InvoiceDto> CreateInvoiceAsync(int orderId, decimal taxAmount = 0, decimal discountAmount = 0)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId);
            if (order == null) throw new BusinessException("سفارش یافت نشد.");
            if (!order.IsConfirmed) throw new BusinessException("سفارش باید تایید شده باشد.");

            if (order.Invoice != null) throw new BusinessException("Invoice قبلا ایجاد شده است.");

            var invoice = new Invoice
            {
                OrderId = order.OrderId,
                InvoiceNumber = $"INV-{DateTime.UtcNow.Ticks}",
                SubTotalAmount = order.TotalAmount,
                TaxAmount = taxAmount,
                DiscountAmount = discountAmount,
                TotalAmount = order.TotalAmount + taxAmount - discountAmount
            };

            await _unitOfWork.Repository<Invoice>().AddAsync(invoice);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Invoice created for order {OrderId}", orderId);
            return _mapper.Map<InvoiceDto>(invoice);
        }
    }
}