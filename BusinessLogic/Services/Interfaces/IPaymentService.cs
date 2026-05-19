using BusinessLogic.DTOs.Payment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IPaymentService
    {
        Task<PaymentDto> RecordPaymentAsync(int invoiceId, decimal amount, string transactionId, CancellationToken cancellationToken = default);
    }
}
