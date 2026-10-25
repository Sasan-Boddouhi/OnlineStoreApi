using Application.Common.Specifications;
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
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<InvoiceService> _logger;

        public InvoiceService(IUnitOfWork unitOfWork, IMapper mapper, ICurrentUserService currentUser, ILogger<InvoiceService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _currentUser = currentUser;
            _logger = logger;
        }

        public async Task<InvoiceDto> CreateInvoiceAsync(int orderId, decimal taxAmount = 0, decimal discountAmount = 0, CancellationToken cancellationToken = default)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId, cancellationToken);

            if (order == null) throw new BusinessException("سفارش یافت نشد.", "ORDER_NOT_FOUND");

            if (order.Status != OrderStatus.Processing)
                throw new BusinessException("سفارش باید تایید شده باشد.", "ORDER_NOT_PROCESSING");

            if (order.Invoice != null) throw new BusinessException("فاکتور قبلاً ایجاد شده است.", "INVOICE_ALREADY_EXISTS");

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

        public async Task<InvoiceDto?> GetByIdAsync(int invoiceId, CancellationToken cancellationToken = default)
        {
            var invoice = await _unitOfWork.Repository<Invoice>()
                .FirstOrDefaultAsync(new Spec<Invoice>().Where(i => i.InvoiceId == invoiceId).Include(i => i.Order), cancellationToken);
            if (invoice == null) return null;
            await EnsureCanAccessAsync(invoice, cancellationToken);
            return _mapper.Map<InvoiceDto>(invoice);
        }

        public async Task<InvoiceDto?> GetByOrderAsync(int orderId, CancellationToken cancellationToken = default)
        {
            var invoice = await _unitOfWork.Repository<Invoice>()
                .FirstOrDefaultAsync(new Spec<Invoice>().Where(i => i.OrderId == orderId).Include(i => i.Order), cancellationToken);
            if (invoice == null) return null;
            await EnsureCanAccessAsync(invoice, cancellationToken);
            return _mapper.Map<InvoiceDto>(invoice);
        }

        public async Task<IEnumerable<InvoiceDto>> GetMyInvoicesAsync(CancellationToken cancellationToken = default)
        {
            var userId = _currentUser.TryGetCurrentUserId();
            if (userId is null) return Enumerable.Empty<InvoiceDto>();

            var customer = (await _unitOfWork.Repository<Customer>()
                .ListAsync(new Spec<Customer>().Where(c => c.UserId == userId.Value), cancellationToken))
                .FirstOrDefault();
            if (customer is null) return Enumerable.Empty<InvoiceDto>();

            var invoices = await _unitOfWork.Repository<Invoice>()
                .ListAsync(new Spec<Invoice>().Where(i => i.Order.CustomerId == customer.CustomerId).Include(i => i.Order), cancellationToken);

            return _mapper.Map<IEnumerable<InvoiceDto>>(invoices);
        }

        private async Task EnsureCanAccessAsync(Invoice invoice, CancellationToken cancellationToken)
        {
            var role = _currentUser.GetCurrentUserRole();
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                return;

            var userId = _currentUser.TryGetCurrentUserId();
            if (userId is null)
                throw new ForbiddenException("دسترسی به فاکتور مجاز نیست.", "INVOICE_FORBIDDEN");
            var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(invoice.Order.CustomerId, cancellationToken);
            if (customer is null || customer.UserId != userId.Value)
                throw new ForbiddenException("دسترسی به فاکتور مجاز نیست.", "INVOICE_FORBIDDEN");
        }
    }
}
