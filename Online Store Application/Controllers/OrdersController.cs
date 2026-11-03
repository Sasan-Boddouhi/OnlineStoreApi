using Asp.Versioning;
using Application.Entities;
using Application.Interfaces;
using BusinessLogic.DTOs.Order;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

[Authorize]
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ICurrentUserService _currentUser;

    public OrdersController(
        IOrderService orderService,
        ICurrentUserService currentUser)
    {
        _orderService = orderService;
        _currentUser = currentUser;
    }

    /// <summary>دریافت فهرست سفارش‌های کاربر جاری.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders(
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetCurrentUserId();
        var orders = await _orderService.GetOrdersAsync(userId, cancellationToken);
        return Ok(orders);
    }

    /// <summary>دریافت فهرست تمام سفارش‌ها با قابلیت فیلتر وضعیت و صفحه‌بندی (مخصوص ادمین).</summary>
    [HttpGet("admin")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(PagedResult<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetAdminOrders(
        [FromQuery] OrderStatus? status,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _orderService.GetAdminOrdersAsync(status, pageNumber, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>دریافت جزئیات یک سفارش بر اساس شناسه.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailsDto>> GetOrderById(
        int id,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetCurrentUserId();
        var order = await _orderService.GetOrderDetailsAsync(
            userId,
            id,
            cancellationToken);

        if (order is null)
            return NotFound();

        return Ok(order);
    }

    /// <summary>
    /// Phase 3: تحویل سفارش و ایجاد تسویهٔ فروشندگان.
    /// عملیات مدیریتی است، پس Policy ادمین لازم دارد.
    /// </summary>
    [HttpPost("{id:int}/deliver")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> Deliver(int id, CancellationToken cancellationToken)
        => Ok(await _orderService.MarkDeliveredAsync(id, cancellationToken));

    /// <summary>تأیید سفارش توسط ادمین.</summary>
    [HttpPost("{id:int}/confirm")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> Confirm(int id, CancellationToken cancellationToken)
        => Ok(await _orderService.ConfirmOrderAsync(id, cancellationToken));

    /// <summary>لغو سفارش.</summary>
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> Cancel(int id, CancellationToken cancellationToken)
        => Ok(await _orderService.CancelOrderAsync(id, cancellationToken));

    /// <summary>مرجوع کردن سفارش توسط ادمین.</summary>
    [HttpPost("{id:int}/return")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> ReturnOrder(int id, CancellationToken cancellationToken)
        => Ok(await _orderService.MarkReturnedAsync(id, cancellationToken));
}
