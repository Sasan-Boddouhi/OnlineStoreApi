using Asp.Versioning;
using Application.Interfaces;
using BusinessLogic.DTOs.Order;
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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders(
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.GetCurrentUserId();
        var orders = await _orderService.GetOrdersAsync(userId, cancellationToken);
        return Ok(orders);
    }

    [HttpGet("{id:int}")]
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
    public async Task<ActionResult<OrderDto>> Deliver(int id, CancellationToken cancellationToken)
        => Ok(await _orderService.MarkDeliveredAsync(id, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(int id, CancellationToken cancellationToken)
        => Ok(await _orderService.CancelOrderAsync(id, cancellationToken));

    [HttpPost("{id:int}/return")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<OrderDto>> ReturnOrder(int id, CancellationToken cancellationToken)
        => Ok(await _orderService.MarkReturnedAsync(id, cancellationToken));
}
