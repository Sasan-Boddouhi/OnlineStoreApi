using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    public sealed class PaymentService : IPaymentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<PaymentService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<PaymentDto> RecordPaymentAsync(int invoiceId, decimal amount, string transactionId)
        {
            var invoice = await _unitOfWork.Repository<Invoice>().GetByIdAsync(invoiceId);
            if (invoice == null) throw new BusinessException("Invoice یافت نشد.");

            var payment = new Payment
            {
                InvoiceId = invoiceId,
                Amount = amount,
                TransactionId = transactionId,
                PaymentDate = DateTime.UtcNow
            };

            invoice.MarkAsPaid();

            await _unitOfWork.Repository<Payment>().AddAsync(payment);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Payment recorded for invoice {InvoiceId}", invoiceId);
            return _mapper.Map<PaymentDto>(payment);
        }
    }
}