using Application.Interfaces;
using BusinessLogic.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Online_Store_Application.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/orders")]
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

    }

}
