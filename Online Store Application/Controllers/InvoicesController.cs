using Asp.Versioning;
using BusinessLogic.DTOs.Invoice;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;

    public InvoicesController(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceService.GetByIdAsync(id, cancellationToken);
        if (invoice == null) return NotFound();
        return Ok(invoice);
    }

    [HttpGet("by-order/{orderId:int}")]
    public async Task<IActionResult> GetByOrder(int orderId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceService.GetByOrderAsync(orderId, cancellationToken);
        if (invoice == null) return NotFound();
        return Ok(invoice);
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMy(CancellationToken cancellationToken = default)
    {
        var invoices = await _invoiceService.GetMyInvoicesAsync(cancellationToken);
        return Ok(invoices);
    }

    [HttpPost("{id:int}/pay")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> MarkAsPaid(int id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceService.MarkAsPaidAsync(id, cancellationToken);
        return Ok(invoice);
    }
}
