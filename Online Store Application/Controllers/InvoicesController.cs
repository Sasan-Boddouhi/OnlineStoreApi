using System.ComponentModel.DataAnnotations;
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

    /// <summary>دریافت پیش‌فاکتور بر اساس شناسه.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceService.GetByIdAsync(id, cancellationToken);
        if (invoice == null) return NotFound();
        return Ok(invoice);
    }

    /// <summary>دریافت پیش‌فاکتور بر اساس شناسه سفارش.</summary>
    [HttpGet("by-order/{orderId:int}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByOrder(int orderId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceService.GetByOrderAsync(orderId, cancellationToken);
        if (invoice == null) return NotFound();
        return Ok(invoice);
    }

    /// <summary>دریافت فاکتورهای کاربر جاری.</summary>
    [HttpGet("my")]
    [ProducesResponseType(typeof(IEnumerable<InvoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMy(CancellationToken cancellationToken = default)
    {
        var invoices = await _invoiceService.GetMyInvoicesAsync(cancellationToken);
        return Ok(invoices);
    }

    /// <summary>صدور فاکتور جدید برای سفارش توسط ادمین.</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var created = await _invoiceService.CreateInvoiceAsync(request.OrderId, request.TaxAmount, request.DiscountAmount, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.InvoiceId }, created);
    }

    /// <summary>علامت‌گذاری فاکتور به عنوان پرداخت‌شده توسط ادمین.</summary>
    [HttpPost("{id:int}/pay")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsPaid(int id, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceService.MarkAsPaidAsync(id, cancellationToken);
        return Ok(invoice);
    }
}

public class CreateInvoiceRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "OrderId must be a positive integer.")]
    public int OrderId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "TaxAmount cannot be negative.")]
    public decimal TaxAmount { get; set; } = 0;

    [Range(0, double.MaxValue, ErrorMessage = "DiscountAmount cannot be negative.")]
    public decimal DiscountAmount { get; set; } = 0;
}
