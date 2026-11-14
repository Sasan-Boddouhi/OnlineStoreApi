using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using Application.Interfaces;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Payment;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 42 (FIN-12, CEO-02) — صف برگشت وجه و صف بررسی دستی پرداخت‌ها، از راه HTTP.
///
///  • <c>POST payments/{id}/refund</c> فقط <b>درخواست</b> برگشت وجه است (دلیل الزامی): Succeeded ⇒ RefundPending.
///  • <c>POST payments/{id}/refund/confirm</c> با شمارهٔ پیگیری بانکی: RefundPending ⇒ Refunded.
///  • <c>GET payments/refunds</c>: صف RefundPending، قدیمی‌ترین درخواست اول.
///  • <c>GET payments/review</c> و <c>POST payments/{id}/review/resolve</c>: پرداخت‌هایی که بررسی دستی می‌خواهند
///    (ستون‌های <see cref="Payment.ReviewRequiredAtUtc"/>…؛ AuditLog فقط مدرک است).
///  • همه فقط برای ادمین.
/// </summary>
public class Phase15Batch42RefundQueueTests : ControllerIntegrationTestBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private AppDbContext DbContext => GetService<AppDbContext>();
    private DateTime ClockNow => Factory.Clock.GetUtcNow().UtcDateTime;

    public Phase15Batch42RefundQueueTests(IntegrationTestFactory<Program> factory) : base(factory)
    {
        Factory.Clock.SetUtcNow(DateTimeOffset.UtcNow);
    }

    // ==================== درخواست برگشت وجه ====================

    [Fact]
    public async Task RefundRequest_OnSucceededPayment_MovesItToRefundPending_NotRefunded()
    {
        var paymentId = await CreatePaidOrderAsync();
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync($"/api/v1/payments/{paymentId}/refund", new { reason = "customer asked by phone" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(paymentId);
        payment.State.Should().Be(PaymentState.RefundPending, "a refund request moves no money; the operator still has to pay it back");
        payment.RefundReason.Should().Be("customer asked by phone");
        payment.RefundedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RefundRequest_WithoutReason_Returns422_AndChangesNothing()
    {
        var paymentId = await CreatePaidOrderAsync();
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync($"/api/v1/payments/{paymentId}/refund", new { reason = "" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.Succeeded);
    }

    [Fact]
    public async Task RefundRequest_OnRefundPendingPayment_Returns409_AndDoesNotConfirmIt()
    {
        var paymentId = await CreatePaidOrderAsync();
        var admin = await AdminClientAsync();
        (await admin.PostAsJsonAsync($"/api/v1/payments/{paymentId}/refund", new { reason = "first request" }))
            .EnsureSuccessStatusCode();

        var second = await admin.PostAsJsonAsync($"/api/v1/payments/{paymentId}/refund", new { reason = "second click" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(second)).Should().Be("PAYMENT_NOT_REFUNDABLE");
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.RefundPending);
    }

    // ==================== صف برگشت وجه ====================

    [Fact]
    public async Task RefundQueue_ListsRefundPendingPayments_OldestRequestFirst()
    {
        var first = await CreatePaidOrderAsync();
        var second = await CreatePaidOrderAsync();

        // مشتری سفارش دوم را زودتر لغو می‌کند؛ صف به ترتیب زمان درخواست است، نه شناسه.
        await CancelAsCustomerAsync(second);
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await CancelAsCustomerAsync(first);

        var admin = await AdminClientAsync();
        var response = await admin.GetAsync("/api/v1/payments/refunds?pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = await PageAsync<PaymentDto>(response);
        var ids = page.Items.Select(p => p.PaymentId).ToList();
        ids.Should().Contain(new[] { first, second });
        ids.IndexOf(second).Should().BeLessThan(ids.IndexOf(first));
        page.Items.Should().OnlyContain(p => p.State == PaymentState.RefundPending);
        page.Items.Single(p => p.PaymentId == second).RefundRequestedAtUtc.Should().NotBeNull();
    }

    // ==================== تأیید برگشت وجه ====================

    [Fact]
    public async Task ConfirmRefund_WithoutBankReference_Returns422()
    {
        var paymentId = await CreateRefundPendingPaymentAsync();
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync($"/api/v1/payments/{paymentId}/refund/confirm", new { bankReference = " " });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.RefundPending);
    }

    [Fact]
    public async Task ConfirmRefund_WithFutureDate_Returns422()
    {
        var paymentId = await CreateRefundPendingPaymentAsync();
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/payments/{paymentId}/refund/confirm",
            new { bankReference = "BANK-123", refundedAtUtc = DateTime.UtcNow.AddDays(2) });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ConfirmRefund_StoresBankReferenceAndDate_AndLeavesTheQueue()
    {
        var paymentId = await CreateRefundPendingPaymentAsync();
        var admin = await AdminClientAsync();
        var bankDate = DateTime.UtcNow.AddHours(-3);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/payments/{paymentId}/refund/confirm",
            new { bankReference = "SATNA-778899", refundedAtUtc = bankDate, note = "paid back from the merchant account" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        DbContext.ChangeTracker.Clear();
        var payment = await PaymentAsync(paymentId);
        payment.State.Should().Be(PaymentState.Refunded);
        payment.RefundReference.Should().Be("SATNA-778899");
        payment.RefundedAtUtc.Should().BeCloseTo(bankDate, TimeSpan.FromSeconds(1));

        var pending = await PageAsync<PaymentDto>(await admin.GetAsync("/api/v1/payments/refunds?pageSize=100"));
        pending.Items.Should().NotContain(p => p.PaymentId == paymentId);

        var all = await PageAsync<PaymentDto>(await admin.GetAsync("/api/v1/payments/refunds?pageSize=100&includeRefunded=true"));
        all.Items.Should().Contain(p => p.PaymentId == paymentId && p.State == PaymentState.Refunded);

        var audit = await DbContext.AuditLog.AsNoTracking()
            .Where(a => a.Action == "Payment.Refunded" && a.EntityId == paymentId.ToString())
            .Select(a => a.Details)
            .ToListAsync();
        audit.Should().ContainSingle().Which.Should().Contain("bankReference=SATNA-778899");
    }

    [Fact]
    public async Task ConfirmRefund_OnSucceededPayment_Returns409NotRefundPending()
    {
        var paymentId = await CreatePaidOrderAsync();
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync($"/api/v1/payments/{paymentId}/refund/confirm", new { bankReference = "BANK-1" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("PAYMENT_NOT_REFUND_PENDING");
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.Succeeded);
    }

    // ==================== صف بررسی دستی ====================

    [Fact]
    public async Task ReviewQueue_ShowsAnUnresolvedVerificationFlaggedByTheSweeper_AndResolveClosesIt()
    {
        var seededOrder = await CreateUnpaidOrderAsync();
        var paymentId = await AddPaymentAsync(
            seededOrder,
            PaymentState.Pending,
            verifyStartedAtUtc: ClockNow.AddHours(-2),
            lastChangedAtUtc: ClockNow.AddHours(-2));

        await GetService<IAbandonedPaymentSweeper>().SweepAsync();
        DbContext.ChangeTracker.Clear();

        var admin = await AdminClientAsync();
        var open = await PageAsync<PaymentReviewDto>(await admin.GetAsync("/api/v1/payments/review?pageSize=100"));
        var row = open.Items.Should().ContainSingle(r => r.PaymentId == paymentId).Subject;
        row.ReviewNote.Should().Contain("verification outcome is unknown");
        row.ReviewResolvedAtUtc.Should().BeNull();

        var noNote = await admin.PostAsJsonAsync($"/api/v1/payments/{paymentId}/review/resolve", new { note = "" });
        noNote.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var resolved = await admin.PostAsJsonAsync(
            $"/api/v1/payments/{paymentId}/review/resolve",
            new { note = "checked in the Zarinpal panel: not paid" });
        resolved.StatusCode.Should().Be(HttpStatusCode.OK, await resolved.Content.ReadAsStringAsync());

        var afterResolve = await PageAsync<PaymentReviewDto>(await admin.GetAsync("/api/v1/payments/review?pageSize=100"));
        afterResolve.Items.Should().NotContain(r => r.PaymentId == paymentId);

        var withResolved = await PageAsync<PaymentReviewDto>(
            await admin.GetAsync("/api/v1/payments/review?pageSize=100&includeResolved=true"));
        withResolved.Items.Should().Contain(r => r.PaymentId == paymentId && r.ReviewResolvedAtUtc != null);

        var again = await admin.PostAsJsonAsync($"/api/v1/payments/{paymentId}/review/resolve", new { note = "again" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(again)).Should().Be("PAYMENT_REVIEW_NOT_OPEN");

        // اجرای بعدی جاب بررسیِ بسته‌شده را دوباره باز نمی‌کند.
        await GetService<IAbandonedPaymentSweeper>().SweepAsync();
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paymentId)).ReviewResolvedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ReviewQueue_ShowsACaptureForACancelledOrder()
    {
        var seededOrder = await CreateUnpaidOrderAsync();
        SetCurrentUser(seededOrder.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seededOrder.OrderId, null);

        // همان سناریوی Batch 4b: Verify اول نتیجهٔ نامعلوم دارد، سفارش در این فاصله لغو می‌شود و Verify دوم پول را برداشته نشان می‌دهد.
        Factory.PaymentGateway.EnqueueVerifyResult(PaymentVerificationResult.TransientError("GATEWAY_TIMEOUT", "timeout"));
        var firstVerify = () => PaymentService.VerifyAsync(initiation.Authority!);
        await firstVerify.Should().ThrowAsync<Application.Exceptions.ServiceUnavailableException>();
        await ForceOrderCancelledAsync(seededOrder.OrderId);
        (await PaymentService.VerifyAsync(initiation.Authority!)).State.Should().Be(PaymentState.Succeeded);
        DbContext.ChangeTracker.Clear();

        var admin = await AdminClientAsync();
        var open = await PageAsync<PaymentReviewDto>(await admin.GetAsync("/api/v1/payments/review?pageSize=100"));

        open.Items.Should().ContainSingle(r => r.PaymentId == initiation.PaymentId)
            .Which.ReviewNote.Should().Contain("order was Cancelled").And.Contain("refundNeeded=true");
    }

    // ==================== دسترسی ====================

    [Theory]
    [InlineData("GET", "/api/v1/payments/refunds")]
    [InlineData("GET", "/api/v1/payments/review")]
    [InlineData("POST", "/api/v1/payments/1/refund/confirm")]
    [InlineData("POST", "/api/v1/payments/1/review/resolve")]
    public async Task QueueEndpoints_AreAdminOnly(string method, string path)
    {
        var customer = await CustomerClientAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new { bankReference = "X", note = "x" });

        var response = await customer.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ==================== helpers ====================

    private sealed record SeededOrder(int OrderId, int BuyerUserId);

    private sealed record Page<T>(List<T> Items, int PageNumber, int PageSize, int TotalCount);

    private static async Task<Page<T>> PageAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Page<T>>(Json))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        // سرویس‌ها کاربر جاری را از FakeCurrentUserService می‌خوانند؛ همان ادمین seed‌شده.
        SetCurrentUser(1, "Admin");
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminTokenAsync());
        return client;
    }

    private async Task<HttpClient> CustomerClientAsync()
    {
        var register = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            PhoneNumber = $"0912{Random.Shared.Next(0, 10_000_000):D7}",
            Password = "Customer@123",
            FirstName = "B42",
            LastName = "Customer",
            DeviceId = "b42-device",
            DateOfBirth = "1370/01/01"
        });
        register.EnsureSuccessStatusCode();
        var token = (await register.Content.ReadFromJsonAsync<AuthResultDto>())!.AccessToken;

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private void SetCurrentUser(int userId, string role)
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    private Task<Payment> PaymentAsync(int paymentId)
        => DbContext.Payment.AsNoTracking().SingleAsync(p => p.PaymentId == paymentId);

    /// <summary>سفارش پرداخت‌شده از مسیر واقعی درگاه (Initiate + Verify)؛ شناسهٔ پرداخت Succeeded.</summary>
    private async Task<int> CreatePaidOrderAsync()
    {
        var seeded = await CreateUnpaidOrderAsync();
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        var initiation = await PaymentService.InitiateAsync(seeded.OrderId, null);
        (await PaymentService.VerifyAsync(initiation.Authority!)).State.Should().Be(PaymentState.Succeeded);
        DbContext.ChangeTracker.Clear();
        PaidOrders[initiation.PaymentId] = seeded;
        return initiation.PaymentId;
    }

    private readonly Dictionary<int, SeededOrder> PaidOrders = new();

    private async Task<int> CreateRefundPendingPaymentAsync()
    {
        var paymentId = await CreatePaidOrderAsync();
        await CancelAsCustomerAsync(paymentId);
        return paymentId;
    }

    private async Task CancelAsCustomerAsync(int paymentId)
    {
        var seeded = PaidOrders[paymentId];
        SetCurrentUser(seeded.BuyerUserId, "Customer");
        await OrderService.CancelOrderAsync(seeded.OrderId);
        DbContext.ChangeTracker.Clear();
        (await PaymentAsync(paymentId)).State.Should().Be(PaymentState.RefundPending);
    }

    private async Task ForceOrderCancelledAsync(int orderId)
    {
        var order = await DbContext.Order.AsTracking().SingleAsync(o => o.OrderId == orderId);
        DbContext.Entry(order).Property(o => o.Status).CurrentValue = OrderStatus.Cancelled;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>پرداختی با وضعیت دلخواه برای سفارش (بدون رکورد AuditLog).</summary>
    private async Task<int> AddPaymentAsync(
        SeededOrder seeded,
        PaymentState state,
        DateTime? verifyStartedAtUtc = null,
        DateTime? lastChangedAtUtc = null)
    {
        var order = await DbContext.Order.AsNoTracking().SingleAsync(o => o.OrderId == seeded.OrderId);
        var invoice = new Invoice
        {
            OrderId = seeded.OrderId,
            InvoiceNumber = $"INV-B42-{Guid.NewGuid():N}",
            SubTotalAmount = order.TotalAmount,
            TotalAmount = order.TotalAmount
        };
        DbContext.Invoice.Add(invoice);
        await DbContext.SaveChangesAsync();

        var payment = new Payment
        {
            Amount = order.TotalAmount,
            OrderId = seeded.OrderId,
            InvoiceId = invoice.InvoiceId,
            Gateway = PaymentGatewayType.Manual,
            TransactionId = $"FAKE-b42-{Guid.NewGuid():N}",
            State = state,
            LastStateChangedAtUtc = lastChangedAtUtc ?? DateTime.UtcNow,
            VerifyStartedAtUtc = verifyStartedAtUtc
        };
        DbContext.Payment.Add(payment);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return payment.PaymentId;
    }

    private async Task<SeededOrder> CreateUnpaidOrderAsync()
    {
        var (sellerUser, _) = await CreateUserAsync();
        var listingId = await CreateSellerWithListingAsync(sellerUser);

        var (buyerUser, buyer) = await CreateUserAsync();
        SetCurrentUser(buyerUser.UserId, "Customer");

        var order = await OrderService.CreateEmptyOrderAsync(buyer.CustomerId, "B42 Buyer", "Tehran", "09120000000");
        await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto { ListingId = listingId, Quantity = 1 });
        DbContext.ChangeTracker.Clear();

        return new SeededOrder(order.OrderId, buyerUser.UserId);
    }

    private async Task<(User User, Customer Customer)> CreateUserAsync()
    {
        var user = new User
        {
            FirstName = "B42",
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

    private async Task<int> CreateSellerWithListingAsync(User sellerUser)
    {
        var seller = new Seller
        {
            UserId = sellerUser.UserId,
            StoreName = $"B42Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);

        var product = new Product
        {
            Name = $"B42-{Guid.NewGuid():N}",
            Price = 150_000m,
            SubcategoryId = 1,
            Unit = UnitOfMeasurement.Piece,
            IsActive = true
        };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();

        var variant = new ProductVariant { ProductId = product.ProductId, Name = "Default", IsActive = true };
        DbContext.ProductVariant.Add(variant);
        await DbContext.SaveChangesAsync();

        var listing = new SellerProduct
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 150_000m,
            Status = SellerProductStatus.Active
        };
        DbContext.SellerProduct.Add(listing);

        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            StreetAddress = "Warehouse St",
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "B42",
            RecipientLastName = "Shop",
            UserId = sellerUser.UserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse { Name = $"W-{Guid.NewGuid():N}", SellerId = seller.SellerId, AddressId = address.AddressId };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listing.ListingId, warehouse.WarehouseId, 10m));
        await DbContext.SaveChangesAsync();

        return listing.ListingId;
    }
}
