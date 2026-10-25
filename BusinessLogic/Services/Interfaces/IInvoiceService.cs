using BusinessLogic.DTOs.Invoice;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IInvoiceService
    {
        Task<InvoiceDto> CreateInvoiceAsync(int orderId, decimal taxAmount = 0, decimal discountAmount = 0, CancellationToken cancellationToken = default);
        Task<InvoiceDto?> GetByIdAsync(int invoiceId, CancellationToken cancellationToken = default);
        Task<InvoiceDto?> GetByOrderAsync(int orderId, CancellationToken cancellationToken = default);
        Task<IEnumerable<InvoiceDto>> GetMyInvoicesAsync(CancellationToken cancellationToken = default);
    }
}
