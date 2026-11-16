using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Order;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 7 — نگاشت خطاها و اقدام‌های ادمین روی سفارش (F-16، F-17).
///
///  • ادمین می‌تواند هر سفارشی را لغو کند؛ غیرادمین فقط سفارش خودش را.
///  • نقض قاعدهٔ دامنه 409 است (نه 500)، با کد.
///  • تعارض‌های وضعیت (سفارش/پرداخت/تسویه/بسته) همه 409 هستند؛ ورودی نامعتبر 422 می‌ماند.
///  • endpoint مرده‌ی <c>POST orders/{id}/confirm</c> حذف شده است.
/// </summary>
public class Phase15Batch07ErrorMappingTests : ControllerIntegrationTestBase
{
    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private IInvoiceService InvoiceService => GetService<IInvoiceService>();
    private ISellerPayoutService PayoutService => GetService<ISellerPayoutService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase15Batch07ErrorMappingTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ==================== F-17: لغو توسط ادمین ====================

    [Fact]
    public async Task Cancel_AdminCancelsAnotherCustomersOrder_Succeeds_AndAuditsTheAdmin()
    {
        var (_, ownerCustomer) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(ownerCustomer.CustomerId);

        SetCurrentUser(1, "Admin");
        var result = await OrderService.CancelOrderAsync(orderId);

        result.Status.Should().Be(OrderStatus.Cancelled.ToString());
        DbContext.ChangeTracker.Clear();
        (await OrderStatusAsync(orderId)).Should().Be(OrderStatus.Cancelled);

        var audit = await DbContext.AuditLog.AsNoTracking()
            .SingleAsync(a => a.Action == "Order.Cancelled" && a.EntityId == orderId.ToString());
        audit.UserId.Should().Be(1, "the acting admin, not the order owner, is recorded");
    }

    [Fact]
    public async Task Cancel_CustomerCancelsAnotherCustomersOrder_StillNotFound()
    {
        var (_, ownerCustomer) = await CreateCustomerAsync();
        var (intruderUser, _) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(ownerCustomer.CustomerId);

        SetCurrentUser(intruderUser.UserId, "Customer");
        var act = () => OrderService.CancelOrderAsync(orderId);

        var ex = await act.Should().ThrowAsync<NotFoundException>();
        ex.Which.Code.Should().Be("ORDER_NOT_FOUND");
        (await OrderStatusAsync(orderId)).Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task Http_Cancel_AdminCancelsAnotherCustomersOrder_Returns200()
    {
        var (_, ownerCustomer) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(ownerCustomer.CustomerId);
        await AuthorizeAsAdminAsync();

        var response = await Client.PostAsync($"/api/v1/orders/{orderId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OrderDto>();
        body!.Status.Should().Be(OrderStatus.Cancelled.ToString());
    }

    // ==================== F-16: 500 → 409 ====================

    [Fact]
    public async Task Http_Cancel_ShippedOrder_Returns409WithRuleCode_NotA500()
    {
        var (_, ownerCustomer) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(ownerCustomer.CustomerId, o => { o.Confirm(); o.MarkShipped(); });
        await AuthorizeAsAdminAsync();

        var response = await Client.PostAsync($"/api/v1/orders/{orderId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(response)).Should().Be("ORDER_INVALID_STATE");
        (await OrderStatusAsync(orderId)).Should().Be(OrderStatus.Shipped);
    }

    [Fact]
    public async Task Cancel_CustomerCancelsShippedOrder_ThrowsRuleViolation()
    {
        var (ownerUser, ownerCustomer) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(ownerCustomer.CustomerId, o => { o.Confirm(); o.MarkShipped(); });

        SetCurrentUser(ownerUser.UserId, "Customer");
        var act = () => OrderService.CancelOrderAsync(orderId);

        var ex = await act.Should().ThrowAsync<BusinessRuleViolationException>();
        ex.Which.Code.Should().Be("ORDER_INVALID_STATE");
    }

    // ==================== F-17: endpoint مرده حذف شد ====================

    [Fact]
    public async Task Http_ConfirmEndpoint_IsRemoved()
    {
        // سفارش مال خود ادمین است، پس endpoint قدیمی برایش 200 برمی‌گرداند؛ پس از حذف مسیر وجود ندارد.
        var adminToken = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var admin = await DbContext.User.AsNoTracking().FirstAsync(u => u.PhoneNumber == "09123456789");
        var customer = await DbContext.Customer.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == admin.UserId);
        if (customer is null)
        {
            customer = new Customer { UserId = admin.UserId };
            DbContext.Customer.Add(customer);
            await DbContext.SaveChangesAsync();
        }

        var orderId = await CreateOrderAsync(customer.CustomerId);

        var response = await Client.PostAsync($"/api/v1/orders/{orderId}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OrderStatusAsync(orderId)).Should().Be(OrderStatus.Pending, "payment is the only way an order is confirmed");
    }

    // ==================== تعارض‌های وضعیت: 400 → 409 ====================

    [Fact]
    public async Task AddItem_ToNonPendingOrder_Returns409OrderNotPending()
    {
        var (user, customer) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(customer.CustomerId, o => o.Confirm());

        SetCurrentUser(user.UserId, "Customer");
        var act = () => OrderService.AddItemToOrderAsync(orderId, new AddOrderItemDto { ListingId = 1, Quantity = 1 });

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_NOT_PENDING");
        ex.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task AdminDeliver_OrderAlreadyDelivered_Returns409OrderNotDeliverable()
    {
        var (_, customer) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(customer.CustomerId, o => { o.Confirm(); o.MarkDelivered(); });
        await FulfilmentTestHelper.AddSucceededPaymentAsync(DbContext, orderId);

        SetCurrentUser(1, "Admin");
        var act = () => OrderService.MarkDeliveredAsync(orderId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_NOT_DELIVERABLE");
        ex.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task CreateInvoice_ForPendingOrder_Returns409OrderNotProcessing()
    {
        var (_, customer) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(customer.CustomerId);

        SetCurrentUser(1, "Admin");
        var act = () => InvoiceService.CreateInvoiceAsync(orderId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_NOT_PROCESSING");
    }

    [Fact]
    public async Task InitiatePayment_ForCancelledOrder_Returns409OrderNotPayable()
    {
        var (user, customer) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(customer.CustomerId, o => o.Cancel());

        SetCurrentUser(user.UserId, "Customer");
        var act = () => PaymentService.InitiateAsync(orderId, null);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("ORDER_NOT_PAYABLE");
    }

    [Fact]
    public async Task VerifyPayment_NotPending_Returns409PaymentNotVerifiable()
    {
        var (user, customer) = await CreateCustomerAsync();
        var orderId = await CreateOrderAsync(customer.CustomerId);
        var payment = await AddPaymentAsync(orderId, PaymentState.Cancelled);

        SetCurrentUser(user.UserId, "Customer");
        var act = () => PaymentService.VerifyAsync(payment.TransactionId!);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("PAYMENT_NOT_VERIFIABLE");
    }

    [Fact]
    public async Task MarkPayoutPaid_WhenAlreadyCancelled_Returns409PayoutInvalidStatus()
    {
        var payoutId = await CreatePayoutAsync(p => p.Cancel());

        SetCurrentUser(1, "Admin");
        var act = () => PayoutService.MarkAsPaidAsync(payoutId, "ref");

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("PAYOUT_INVALID_STATE"); // Batch 44: یک کد برای همهٔ رد شدن‌های وضعیت تسویه (پیش‌تر سرویس PAYOUT_INVALID_STATUS و موجودیت PAYOUT_INVALID_STATE می‌داد)
    }

    [Fact]
    public async Task CancelPayout_WhenAlreadyPaid_Returns409PayoutInvalidStatus()
    {
        var payoutId = await CreatePayoutAsync(p => p.MarkPaid("ref", 0m, p.AvailableAtUtc));

        SetCurrentUser(1, "Admin");
        var act = () => PayoutService.CancelPayoutAsync(payoutId);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be("PAYOUT_INVALID_STATE"); // Batch 44: یک کد برای همهٔ رد شدن‌های وضعیت تسویه (پیش‌تر سرویس PAYOUT_INVALID_STATUS و موجودیت PAYOUT_INVALID_STATE می‌داد)
    }

    // ==================== ابزار ====================

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private async Task AuthorizeAsAdminAsync()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private Task<OrderStatus> OrderStatusAsync(int orderId)
        => DbContext.Order.AsNoTracking().Where(o => o.OrderId == orderId).Select(o => o.Status).SingleAsync();

    private async Task<(User User, Customer Customer)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "B7",
            LastName = "User",
            PhoneNumber = $"09{Random.Shared.Next(100000000, 999999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();

        return (user, customer);
    }

    private async Task<int> CreateOrderAsync(int customerId, Action<Order>? configure = null)
    {
        var order = new Order
        {
            CustomerId = customerId,
            ShippingFullName = "B7 Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000"
        };
        configure?.Invoke(order);
        DbContext.Order.Add(order);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return order.OrderId;
    }

    private async Task<Payment> AddPaymentAsync(int orderId, PaymentState state)
    {
        var invoice = new Invoice
        {
            OrderId = orderId,
            InvoiceNumber = $"INV-B7-{Guid.NewGuid():N}"[..30],
            SubTotalAmount = 100m,
            TotalAmount = 100m
        };
        DbContext.Invoice.Add(invoice);
        await DbContext.SaveChangesAsync();

        var payment = new Payment
        {
            Amount = 100m,
            OrderId = orderId,
            InvoiceId = invoice.InvoiceId,
            State = state,
            LastStateChangedAtUtc = DateTime.UtcNow,
            TransactionId = $"B7-{Guid.NewGuid():N}"
        };
        DbContext.Payment.Add(payment);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return payment;
    }

    private async Task<int> CreatePayoutAsync(Action<SellerPayout> configure)
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B7Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();

        var payout = new SellerPayout(seller.SellerId, null, 100m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        configure(payout);
        DbContext.SellerPayout.Add(payout);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return payout.SellerPayoutId;
    }
}
