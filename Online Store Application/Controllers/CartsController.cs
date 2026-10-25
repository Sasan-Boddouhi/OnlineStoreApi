using Asp.Versioning;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Order;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers;

/// <summary>
/// Phase 4 — سبد خرید.
///
/// سبد مهمان با هدر <c>X-Cart-Session</c> شناسایی می‌شود و سبد کاربر با توکن.
/// اگر هر دو حاضر باشند، توکن مقدم است و سبد مهمان فقط از طریق
/// <c>POST /carts/merge</c> ادغام می‌شود — تا کاربر ناخواسته سبد قبلی‌اش را از دست ندهد.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/carts")]
public class CartsController : ControllerBase
{
    /// <summary>هدر شناسهٔ نشست برای سبد مهمان.</summary>
    public const string SessionHeader = "X-Cart-Session";

    private const int MaxSessionIdLength = 64;

    private readonly ICartService _cartService;
    private readonly ICurrentUserService _currentUser;

    public CartsController(ICartService cartService, ICurrentUserService currentUser)
    {
        _cartService = cartService;
        _currentUser = currentUser;
    }

    private int? UserId => _currentUser.TryGetCurrentUserId();

    private string? SessionId
    {
        get
        {
            var value = Request.Headers[SessionHeader].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (value.Length > MaxSessionIdLength)
                throw new BusinessException(
                    $"شناسهٔ نشست حداکثر {MaxSessionIdLength} کاراکتر است.", "CART_SESSION_INVALID");

            return value;
        }
    }

    /// <summary>سبد فعلی؛ برای مهمان ساخته می‌شود.</summary>
    [HttpGet("current")]
    [AllowAnonymous]
    public async Task<ActionResult<CartDto>> GetCurrent(CancellationToken cancellationToken)
        => Ok(await _cartService.GetCartAsync(UserId, SessionId, cancellationToken));

    /// <summary>افزودن قلم به سبد.</summary>
    [HttpPost("items")]
    [AllowAnonymous]
    public async Task<ActionResult<CartDto>> AddItem(
        [FromBody] AddCartItemDto dto,
        CancellationToken cancellationToken)
        => Ok(await _cartService.AddItemAsync(UserId, SessionId, dto, cancellationToken));

    /// <summary>تغییر تعداد یک قلم.</summary>
    [HttpPut("items/{listingId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<CartDto>> UpdateQuantity(
        int listingId,
        [FromBody] UpdateCartItemDto dto,
        CancellationToken cancellationToken)
        => Ok(await _cartService.UpdateQuantityAsync(UserId, SessionId, listingId, dto.Quantity, cancellationToken));

    /// <summary>حذف یک قلم.</summary>
    [HttpDelete("items/{listingId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<CartDto>> RemoveItem(int listingId, CancellationToken cancellationToken)
        => Ok(await _cartService.RemoveItemAsync(UserId, SessionId, listingId, cancellationToken));

    /// <summary>خالی‌کردن سبد.</summary>
    [HttpDelete("current")]
    [AllowAnonymous]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        await _cartService.ClearCartAsync(UserId, SessionId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// ادغام سبد مهمان در سبد کاربر پس از ورود.
    /// شناسهٔ نشست از هدر خوانده می‌شود، نه از بدنه — تا کاربر نتواند
    /// سبد مهمانِ دیگری را به حساب خودش منتقل کند.
    /// </summary>
    [HttpPost("merge")]
    [Authorize]
    public async Task<ActionResult<CartDto>> Merge(CancellationToken cancellationToken)
    {
        var userId = UserId ?? throw new BusinessException("کاربر شناسایی نشد.", "UNAUTHENTICATED");
        var sessionId = SessionId;

        if (sessionId is null)
            return Ok(await _cartService.GetCartAsync(userId, null, cancellationToken));

        return Ok(await _cartService.MergeAnonymousCartAsync(sessionId, userId, cancellationToken));
    }

    /// <summary>تبدیل سبد به سفارش.</summary>
    [HttpPost("checkout")]
    [Authorize]
    public async Task<ActionResult<OrderDto>> Checkout(
        [FromBody] CheckoutDto dto,
        CancellationToken cancellationToken)
        => Ok(await _cartService.CheckoutAsync(UserId, SessionId, dto, cancellationToken));
}