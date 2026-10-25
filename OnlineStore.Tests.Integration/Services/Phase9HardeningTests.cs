using Application.Entities;
using Application.Interfaces;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Seller;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 9 — Hardening: R38/R39 (پاک‌سازی پرداخت)، R32 (صفحه‌بندی)،
/// R40 (گسترش AuditLog)، R44 (پاک‌سازی پرداخت رهاشده)، R33 (کش امتیاز).
/// </summary>
public class Phase9HardeningTests : BaseIntegrationTest
{
    private IProductService ProductService => GetService<IProductService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private IOrderService OrderService => GetService<IOrderService>();
    private IReviewService ReviewService => GetService<IReviewService>();
    private IReviewRatingCacheService RatingCache => GetService<IReviewRatingCacheService>();
    private IAbandonedPaymentSweeper Sweeper => GetService<IAbandonedPaymentSweeper>();
    private ISellerDashboardService DashboardService => GetService<ISellerDashboardService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public Phase9HardeningTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private void SetCurrentUser(int userId, string role = "Customer")
    {
        var fake = (FakeCurrentUserService)GetService<ICurrentUserService>();
        fake.CurrentUserId = userId;
        fake.CurrentUserRole = role;
    }

    // ==================== R38 / R39 — پاک‌سازی پرداخت ====================

    [Fact]
    public void Payment_StatusColumn_IsGone()
    {
        // R39: منبع حقیقت فقط State است. بازگشت Status یک رگرسیون است.
        typeof(Payment).GetProperty("Status").Should().BeNull();
    }

    [Fact]
    public async Task Payment_TwoRowsWithNullTransactionId_BothPersist()
    {
        // R38: اگر ایندکس یکتا فیلتر نداشت، SQL تنها یک NULL را می‌پذیرفت
        // و دومین پرداخت بدون Authority با استثنا شکست می‌خورد.
        var firstInvoice = await CreateInvoiceAsync(10m);
        var secondInvoice = await CreateInvoiceAsync(20m);

        DbContext.Payment.Add(new Payment { Amount = 10m, InvoiceId = firstInvoice });
        DbContext.Payment.Add(new Payment { Amount = 20m, InvoiceId = secondInvoice });

        var act = async () => await DbContext.SaveChangesAsync();

        await act.Should().NotThrowAsync();

        var count = await DbContext.Payment.AsNoTracking()
            .CountAsync(p => p.TransactionId == null);

        count.Should().Be(2);
    }

    // ==================== R32 — صفحه‌بندی داشبورد ====================

    [Fact]
    public async Task TopProducts_PaginatesWithMetadata()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        SetCurrentUser(sellerUser.UserId, "Seller");

        // سه واریانت مستقل، هر کدام با فروش متفاوت.
        for (var i = 3; i >= 1; i--)
        {
            var listing = await CreateListingAsync(seller, 10m);
            await CreateDeliveredOrderAsync(listing, quantity: i);
        }

        SetCurrentUser(sellerUser.UserId, "Seller");

        var firstPage = await DashboardService.GetTopProductsAsync(seller.SellerId, 1, 2);
        var secondPage = await DashboardService.GetTopProductsAsync(seller.SellerId, 2, 2);

        firstPage.Items.Should().HaveCount(2);
        firstPage.PageNumber.Should().Be(1);
        firstPage.PageSize.Should().Be(2);
        firstPage.TotalCount.Should().Be(3);
        firstPage.TotalPages.Should().Be(2);

        // صفحهٔ اول بهترین فروش را دارد (۳ عدد)، صفحهٔ دوم بقیه را.
        firstPage.Items[0].QuantitySold.Should().Be(3);

        secondPage.Items.Should().HaveCount(1);
        secondPage.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task TopProducts_PageBeyondLast_ReturnsEmptyButKeepsTotal()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        SetCurrentUser(sellerUser.UserId, "Seller");
        var listing = await CreateListingAsync(seller, 10m);
        await CreateDeliveredOrderAsync(listing, quantity: 2);

        SetCurrentUser(sellerUser.UserId, "Seller");

        var page = await DashboardService.GetTopProductsAsync(seller.SellerId, 99, 10);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(1);
    }

    // ==================== R40 — AuditLog ====================

    [Fact]
    public async Task ProductPriceChange_WritesAuditLog()
    {
        var (_, _) = await CreateCustomerAsync();
        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"P9-PRICE-{Guid.NewGuid():N}", Price = 100m, SubcategoryId = 1
        });

        await ProductService.UpdateAsync(new UpdateProductDto
        {
            ProductId = product.ProductId,
            Name = product.Name,
            Price = 250m,
            SubcategoryId = 1
        });

        var log = await DbContext.AuditLog.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Action == "Product.PriceChanged"
                                   && a.EntityId == product.ProductId.ToString());

        log.Should().NotBeNull();
        log!.EntityName.Should().Be(nameof(Product));
        log.Details.Should().Contain("from=100").And.Contain("to=250");
    }

    [Fact]
    public async Task ProductUpdate_WithoutPriceChange_WritesNoAudit()
    {
        // بدون این تست، «همیشه حسابرسی کن» می‌توانست لاگ را پر کند از نویز.
        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"P9-NOPRICE-{Guid.NewGuid():N}", Price = 100m, SubcategoryId = 1
        });

        await ProductService.UpdateAsync(new UpdateProductDto
        {
            ProductId = product.ProductId,
            Name = product.Name,
            Price = 100m,
            SubcategoryId = 1
        });

        var any = await DbContext.AuditLog.AsNoTracking()
            .AnyAsync(a => a.Action == "Product.PriceChanged"
                        && a.EntityId == product.ProductId.ToString());

        any.Should().BeFalse();
    }

    [Fact]
    public async Task ProductDelete_WritesAuditLog()
    {
        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"P9-DEL-{Guid.NewGuid():N}", Price = 50m, SubcategoryId = 1
        });

        (await ProductService.DeleteAsync(product.ProductId)).Should().BeTrue();

        var log = await DbContext.AuditLog.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Action == "Product.Deleted"
                                   && a.EntityId == product.ProductId.ToString());

        log.Should().NotBeNull();
    }

    [Fact]
    public async Task OrderCancellation_WritesAuditLog()
    {
        var (order, buyerUserId) = await CreatePayableOrderAsync(80m);

        SetCurrentUser(buyerUserId);

        await OrderService.CancelOrderAsync(order.OrderId);

        var log = await DbContext.AuditLog.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Action == "Order.Cancelled"
                                   && a.EntityId == order.OrderId.ToString());

        log.Should().NotBeNull();
        log!.EntityName.Should().Be(nameof(Order));
    }

    // ==================== R44 — پاک‌سازی پرداخت رهاشده ====================

    [Fact]
    public async Task Sweep_CancelsAbandonedPendingPayments()
    {
        var stale = await CreatePaymentAsync(PaymentState.Pending, DateTime.UtcNow.AddHours(-2));
        var fresh = await CreatePaymentAsync(PaymentState.Pending, DateTime.UtcNow.AddMinutes(-1));

        var swept = await Sweeper.SweepAsync();

        swept.Should().BeGreaterThanOrEqualTo(1);

        await DbContext.Entry(stale).ReloadAsync();
        await DbContext.Entry(fresh).ReloadAsync();

        stale.State.Should().Be(PaymentState.Cancelled);
        fresh.State.Should().Be(PaymentState.Pending);
    }

    [Fact]
    public async Task Sweep_LeavesSucceededPaymentsAlone()
    {
        var succeeded = await CreatePaymentAsync(PaymentState.Succeeded, DateTime.UtcNow.AddDays(-5));

        await Sweeper.SweepAsync();

        await DbContext.Entry(succeeded).ReloadAsync();
        succeeded.State.Should().Be(PaymentState.Succeeded);
    }

    [Fact]
    public async Task Sweep_WritesAuditLogForCancelledPayment()
    {
        var stale = await CreatePaymentAsync(PaymentState.Pending, DateTime.UtcNow.AddHours(-3));

        await Sweeper.SweepAsync();

        var log = await DbContext.AuditLog.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Action == "Payment.Cancelled"
                                   && a.EntityId == stale.PaymentId.ToString());

        log.Should().NotBeNull();
        log!.Details.Should().Contain("abandoned");
    }

    // ==================== R33 — کش امتیاز ====================

    [Fact]
    public async Task ApproveProductReview_UpdatesProductRatingCache()
    {
        var (productId, orderItemId) = await CreateReviewableProductAsync();
        var review = await SeedProductReviewAsync(productId, orderItemId, 5);

        // پیش از تأیید، کش باید صفر بماند.
        (await DbContext.Product.AsNoTracking()
            .Where(x => x.ProductId == productId)
            .Select(x => x.ReviewCount).FirstAsync()).Should().Be(0);

        await ReviewService.ApproveProductReviewAsync(review.ProductReviewId);

        var cached = await DbContext.Product.AsNoTracking()
            .FirstAsync(x => x.ProductId == productId);

        cached.ReviewCount.Should().Be(1);
        cached.AverageRating.Should().Be(5d);
    }

    [Fact]
    public async Task UnapprovedProductReview_DoesNotAffectCachedRating()
    {
        var (productId, orderItemId) = await CreateReviewableProductAsync();

        // ایندکس یکتا روی (ProductId, CustomerId) است، پس یک نظر کافی است.
        await SeedProductReviewAsync(productId, orderItemId, 4);

        await RatingCache.RecalculateProductAsync(productId);

        var cached = await DbContext.Product.AsNoTracking()
            .FirstAsync(x => x.ProductId == productId);

        cached.ReviewCount.Should().Be(0);
        cached.AverageRating.Should().Be(0d);
    }

    [Fact]
    public async Task ProductDetails_ReadsRatingFromCache()
    {
        // R33: زیرکوئری AVG حذف شده؛ اگر projection به ستون کش برنگردد، این تست
        // عدد 0 نشان می‌دهد چون نظری وجود ندارد ولی کش دستی ست شده است.
        var (productId, _) = await CreateReviewableProductAsync();

        var tracked = await DbContext.Product.FirstAsync(x => x.ProductId == productId);
        tracked.AverageRating = 4.5d;
        tracked.ReviewCount = 12;
        await DbContext.SaveChangesAsync();

        SetCurrentUser(1, "Admin");
        var dto = await ProductService.GetByIdAsync(productId);

        dto.Should().NotBeNull();
        dto!.ReviewCount.Should().Be(12);
        dto.AverageRating.Should().Be(4.5d);
    }

    [Fact]
    public async Task ApproveSellerReview_UpdatesSellerRatingCache()
    {
        var (sellerUser, customer) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        var review = new SellerReview
        {
            SellerId = seller.SellerId,
            CustomerId = customer.CustomerId,
            Rating = 3,
            Comment = "P9",
            IsApproved = false
        };
        DbContext.SellerReview.Add(review);
        await DbContext.SaveChangesAsync();

        await ReviewService.ApproveSellerReviewAsync(review.SellerReviewId);

        var cached = await DbContext.Seller.AsNoTracking()
            .FirstAsync(s => s.SellerId == seller.SellerId);

        cached.ReviewCount.Should().Be(1);
        cached.AverageRating.Should().Be(3d);
    }

    // ==================== ابزار ====================

    /// <summary>
    /// Payment.InvoiceId یک FK اجباری است؛ بدون فاکتور واقعی، INSERT با
    /// «FOREIGN KEY constraint failed» شکست می‌خورد.
    /// </summary>
    private async Task<Payment> CreatePaymentAsync(PaymentState state, DateTime lastChangedUtc)
    {
        var invoiceId = await CreateInvoiceAsync(50m);

        var payment = new Payment
        {
            Amount = 50m,
            InvoiceId = invoiceId,
            State = state,
            LastStateChangedAtUtc = lastChangedUtc,
            TransactionId = $"P9-{Guid.NewGuid():N}"
        };

        DbContext.Payment.Add(payment);
        await DbContext.SaveChangesAsync();

        return payment;
    }

    private async Task<int> CreateInvoiceAsync(decimal amount)
    {
        var (order, _) = await CreatePayableOrderAsync(amount);

        var invoice = new Invoice
        {
            OrderId = order.OrderId,
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            SubTotalAmount = amount,
            TotalAmount = amount
        };

        DbContext.Invoice.Add(invoice);
        await DbContext.SaveChangesAsync();

        return invoice.InvoiceId;
    }

    private async Task<(int productId, int orderItemId)> CreateReviewableProductAsync()
    {
        var (order, buyerUser) = await CreatePayableOrderAsync(60m);

        var orderItemId = await DbContext.OrderItem.AsNoTracking()
            .Where(i => i.OrderId == order.OrderId)
            .Select(i => i.OrderItemId)
            .FirstAsync();

        var productId = await DbContext.OrderItem.AsNoTracking()
            .Where(i => i.OrderItemId == orderItemId)
            .Join(DbContext.SellerProduct, i => i.ListingId, l => l.ListingId,
                  (i, l) => l.VariantId)
            .Join(DbContext.ProductVariant, vId => vId, v => v.VariantId,
                  (vId, v) => v.ProductId)
            .FirstAsync();

        SetCurrentUser(buyerUser);

        return (productId, orderItemId);
    }

    private async Task<ProductReview> SeedProductReviewAsync(int productId, int orderItemId, int rating)
    {
        var customerId = await DbContext.OrderItem.AsNoTracking()
            .Where(i => i.OrderItemId == orderItemId)
            .Join(DbContext.Order, i => i.OrderId, o => o.OrderId, (i, o) => o.CustomerId)
            .FirstAsync();

        var review = new ProductReview
        {
            ProductId = productId,
            CustomerId = customerId,
            OrderItemId = orderItemId,
            Rating = rating,
            Comment = "P9 review",
            IsApproved = false
        };

        DbContext.ProductReview.Add(review);
        await DbContext.SaveChangesAsync();

        return review;
    }

    private async Task<(User user, Customer customer)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "P9",
            LastName = "User",
            PhoneNumber = $"0913{new Random().Next(1000000, 9999999)}",
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
            StoreName = $"P9Shop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();
        return seller;
    }

    private async Task<(BusinessLogic.DTOs.Order.OrderDto order, int buyerUserId)> CreatePayableOrderAsync(decimal price)
    {
        var (buyerUser, buyerCustomer) = await CreateCustomerAsync();
        var listing = await CreateListingAsync(null, price);

        SetCurrentUser(buyerUser.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(
            buyerCustomer.CustomerId, "P9 Buyer", "Tehran", "09120000000");

        await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto
        {
            ListingId = listing.ListingId,
            Quantity = 1
        });

        return (order, buyerUser.UserId);
    }

    private async Task<SellerProductDto> CreateListingAsync(Seller? seller, decimal price)
    {
        if (seller is null)
        {
            var (sellerUser, _) = await CreateCustomerAsync();
            seller = await CreateSellerAsync(sellerUser.UserId);
        }

        var product = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = $"P9-{Guid.NewGuid():N}", Price = price, SubcategoryId = 1
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

    private async Task CreateDeliveredOrderAsync(SellerProductDto listing, int quantity)
    {
        var (buyerUser, buyerCustomer) = await CreateCustomerAsync();
        SetCurrentUser(buyerUser.UserId);

        var order = await OrderService.CreateEmptyOrderAsync(
            buyerCustomer.CustomerId, "P9 Buyer", "Tehran", "09120000000");

        await OrderService.AddItemToOrderAsync(order.OrderId, new AddOrderItemDto
        {
            ListingId = listing.ListingId,
            Quantity = quantity
        });

        await OrderService.ConfirmOrderAsync(order.OrderId);
        await OrderService.MarkDeliveredAsync(order.OrderId);
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
                RecipientFirstName = "P9",
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