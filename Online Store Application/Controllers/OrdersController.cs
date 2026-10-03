using Asp.Versioning;
using Application.Interfaces;
using Application.DTOs.Order;
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
}
