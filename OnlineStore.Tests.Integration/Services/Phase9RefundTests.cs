using System.Net;
using System.Net.Http.Json;
using Application.Entities;
using Application.Interfaces;
using Application.Exceptions;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Seller;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 9 (R43) — برگشت وجه توسط ادمین.
///
/// این قابلیت پول را جابه‌جا نمی‌کند (زرین‌پال برگشت خودکار ندارد) و هدفش
/// هم‌راست کردن سیستم با واقعیت است. تست‌ها همان چهار اثر جانبی را می‌سنجند.
/// </summary>
public class Phase9RefundTests : BaseIntegrationTest
{
    private IProductService ProductService => GetService<IProductService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private IOrderService OrderService => GetService<IOrderService>();
    private IPaymentService PaymentService => GetService<IPaymentService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase9RefundTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private void SetCurrentUser(int userId, string role = "Customer")
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    // ==================== اثر ۱ و ۲: پرداخت و سفارش ====================

    [Fact]
    public async Task Refund_DeliveredOrder_MarksPaymentRefundedAndOrderReturned()
    {
        var (payment, orderId) = await CreateSucceededPaymentForDeliveredOrderAsync();

        SetCurrentUser(1, "Admin");
        var dto = await PaymentService.RefundPaymentAsync(payment.PaymentId, "customer complaint");

        dto.State.Should().Be(PaymentState.Refunded);

        var order = await DbContext.Order.AsNoTracking().FirstAsync(o => o.OrderId == orderId);
        order.Status.Should().Be(OrderStatus.Returned);
    }

    [Fact]
    public async Task Refund_PendingOrder_CancelsOrder()
    {
        var (payment, orderId) = await CreateSucceededPaymentAsync(confirmOrder: false);

        SetCurrentUser(1, "Admin");
        await PaymentService.RefundPaymentAsync(payment.PaymentId, "duplicate charge");

        var order = await DbContext.Order.AsNoTracking().FirstAsync(o => o.OrderId == orderId);
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    // ==================== اثر ۳: تسویه فروشنده ====================

    [Fact]
    public async Task Refund_CancelsPendingSellerPayouts()
    {
        var (payment, orderId) = await CreateSucceededPaymentForDeliveredOrderAsync();

        var group = await DbContext.OrderSellerGroup.AsNoTracking().FirstAsync(g => g.OrderId == orderId);

        var payout = new SellerPayout(
            group.SellerId, group.OrderSellerGroupId, 80m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        DbContext.SellerPayout.Add(payout);
        await DbContext.SaveChangesAsync();

        SetCurrentUser(1, "Admin");
        await PaymentService.RefundPaymentAsync(payment.PaymentId, "return");

        var reloaded = await DbContext.SellerPayout.AsNoTracking()
            .FirstAsync(p => p.SellerPayoutId == payout.SellerPayoutId);

        reloaded.Status.Should().Be(PayoutStatus.Cancelled);

        // مبلغ منفی ساخته نمی‌شود: invariant موجودیت حفظ می‌شود.
        var negative = await DbContext.SellerPayout.AsNoTracking().AnyAsync(p => p.Amount < 0);
        negative.Should().BeFalse();
    }

    // ==================== اثر ۴: حسابرسی ====================

    [Fact]
    public async Task Refund_WritesAuditLogFlaggingManualMoneyMovement()
    {
        var (payment, _) = await CreateSucceededPaymentAsync(confirmOrder: false);

        SetCurrentUser(1, "Admin");
        await PaymentService.RefundPaymentAsync(payment.PaymentId, "bank transfer backfired");

        var log = await DbContext.AuditLog.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Action == "Payment.Refunded"
                                   && a.EntityId == payment.PaymentId.ToString());

        log.Should().NotBeNull();
        log!.Details.Should().Contain("MONEY_MOVED_MANUALLY=true");
        log.Details.Should().Contain("bank transfer backfired");
    }

    // ==================== حالت‌های نامعتبر ====================

    [Fact]
    public async Task Refund_PaymentNotFound_ThrowsNotFound()
    {
        SetCurrentUser(1, "Admin");

        var act = async () => await PaymentService.RefundPaymentAsync(999_999);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(PaymentState.Initiated)]
    [InlineData(PaymentState.Pending)]
    [InlineData(PaymentState.Failed)]
    [InlineData(PaymentState.Cancelled)]
    [InlineData(PaymentState.Refunded)]
    public async Task Refund_NonSucceededPayment_IsRejected(PaymentState state)
    {
        var (orderId, _) = await CreateOrderAsync();
        var invoiceId = await CreateInvoiceAsync(orderId);

        var payment = new Payment
        {
            Amount = 100m,
            InvoiceId = invoiceId,
            OrderId = orderId,
            State = state,
            LastStateChangedAtUtc = DateTime.UtcNow,
            TransactionId = $"P9R-{Guid.NewGuid():N}"
        };
        DbContext.Payment.Add(payment);
        await DbContext.SaveChangesAsync();

        SetCurrentUser(1, "Admin");

        var act = async () => await PaymentService.RefundPaymentAsync(payment.PaymentId);

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.Which.Code.Should().Be("PAYMENT_NOT_REFUNDABLE");
    }

    // ==================== کنترل دسترسی (endpoint) ====================

    [Fact]
    public async Task RefundEndpoint_AnonymousUser_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync(
            $"/api/v1/payments/1/refund", new { reason = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ==================== ابزار ====================

    private async Task<(Payment payment, int orderId)> CreateSucceededPaymentForDeliveredOrderAsync()
    {
        var (orderId, _) = await CreateOrderAsync();

        await OrderService.ConfirmOrderAsync(orderId);
        await OrderService.MarkDeliveredAsync(orderId);

        var invoiceId = await CreateInvoiceAsync(orderId);
        var payment = await AddSucceededPaymentAsync(orderId, invoiceId);

        return (payment, orderId);
    }

    private async Task<(Payment payment, int orderId)> CreateSucceededPaymentAsync(bool confirmOrder)
    {
        var (orderId, _) = await CreateOrderAsync();

        if (confirmOrder)
            await OrderService.ConfirmOrderAsync(orderId);

        var invoiceId = await CreateInvoiceAsync(orderId);
        var payment = await AddSucceededPaymentAsync(orderId, invoiceId);

        return (payment, orderId);
    }

    private async Task<Payment> AddSucceededPaymentAsync(int orderId, int invoiceId)
    {
        var payment = new Payment
        {
            Amount = 100m,
            InvoiceId = invoiceId,
            OrderId = orderId,
            State = PaymentState.Succeeded,
            LastStateChangedAtUtc = DateTime.UtcNow,
            TransactionId = $"P9R-{Guid.NewGuid():N}",
            GatewayReference = "REF-P9"
        };

        DbContext.Payment.Add(payment);
        await DbContext.SaveChangesAsync();

        return payment;
    }

    private async Task<int> CreateInvoiceAsync(int orderId)
    {
        var invoice = new Invoice
        {
            OrderId = orderId,
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            SubTotalAmount = 100m,
            TotalAmount = 100m
        };

        DbContext.Invoice.Add(invoice);
        await DbContext.SaveChangesAsync();

        return invoice.InvoiceId;
    }

    private async Task<(int orderId, int buyerUserId)> CreateOrderAsync()
    {
        var (buyerUser, buyerCustomer) = await CreateCustomerAsync();
        var listing = await CreateListingAsync(100m);

        SetCurrentUser(buyerUser.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(
            buyerCustomer.CustomerId, "P9R Buyer", "Tehran", "09120000000");

        await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto
        {
            ListingId = listing.ListingId,
            Quantity = 1
        });

        return (order.OrderId, buyerUser.UserId);
    }

    private async Task<SellerProductDto> CreateListingAsync(decimal price)
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"P9R-{Guid.NewGuid():N}", Price = price, SubcategoryId = 1
        });

        var variant = (await ProductService.GetVariantsAsync(product.ProductId)).Single();

        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = price,
            Status = SellerProductStatus.Active
        });

        await EnsureStockAsync(seller, listing.ListingId);
        return listing;
    }

    private async Task<(User user, Customer customer)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "P9R",
            LastName = "User",
            PhoneNumber = $"0914{new Random().Next(1000000, 9999999)}",
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

    private async Task<Seller> CreateSellerAsync(int ownerUserId)
    {
        var seller = new Seller
        {
            UserId = ownerUserId,
            StoreName = $"P9RShop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();
        return seller;
    }

    private async Task EnsureStockAsync(Seller seller, int listingId)
    {
        var warehouse = await DbContext.Warehouse.FirstOrDefaultAsync(w => w.SellerId == seller.SellerId);

        if (warehouse is null)
        {
            var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
            DbContext.Province.Add(province);
            await DbContext.SaveChangesAsync();

            var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
            DbContext.City.Add(city);
            await DbContext.SaveChangesAsync();

            var address = new Address
            {
                CityId = city.CityId,
                Plaque = "1",
                Unit = "1",
                PostalCode = "1234567890",
                RecipientFirstName = "P9R",
                RecipientLastName = "Shop",
                UserId = seller.UserId
            };
            DbContext.Address.Add(address);
            await DbContext.SaveChangesAsync();

            warehouse = new Warehouse
            {
                Name = $"W-{Guid.NewGuid():N}",
                AddressId = address.AddressId,
                SellerId = seller.SellerId
            };
            DbContext.Warehouse.Add(warehouse);
            await DbContext.SaveChangesAsync();
        }

        DbContext.Inventory.Add(new Inventory(listingId, warehouse.WarehouseId, 100));
        await DbContext.SaveChangesAsync();
    }
}