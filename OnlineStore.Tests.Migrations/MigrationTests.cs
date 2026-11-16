using DataLayer.Context;
using FluentAssertions;

namespace OnlineStore.Tests.Migrations;

/// <summary>
/// Phase 9 (R25) — تست واقعی مایگریشن‌ها روی SQL Server.
///
/// چرا این پروژه لازم بود: تمام تست‌های یکپارچه از <c>EnsureCreated</c> استفاده می‌کنند،
/// یعنی **هیچ‌کدام مایگریشن را اجرا نمی‌کنند**. سه باگ تاریخ‌دار پروژه (نبود backfill
/// در فاز ۸) از همین شکاف بیرون آمده بودند.
///
/// این تست‌ها از اسکیمای قبلی شروع می‌کنند، داده می‌کارند، مایگریشن را اعمال می‌کنند،
/// داده را می‌سنجند، سپس rollback می‌زنند و دوباره می‌سنجند.
/// </summary>
[Trait("Category", "Migration")]
public class MigrationTests : IClassFixture<SqlServerContainerFixture>
{
    private readonly SqlServerContainerFixture _server;

    public MigrationTests(SqlServerContainerFixture server) => _server = server;

    // ==================== Phase 2 ====================

    [Fact]
    public async Task Phase2_OwnershipFoundation_AppliesAndRollsBack()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateToAsync("20261021063020_Phase2_OwnershipFoundation");

        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'Seller'"))
            .Should().Be(1, "فاز ۲ جدول فروشنده را می‌سازد");

        await db.MigrateToAsync("20261021060949_Phase1_RbacFoundation");

        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'Seller'"))
            .Should().Be(0, "rollback باید جدول را بردارد");
    }

    // ==================== Phase 6 ====================

    [Fact]
    public async Task Phase6_VariantsAndAttributes_AppliesAndRollsBack()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateToAsync("20261021075900_Phase6_VariantsAndAttributes");

        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'ProductVariant'"))
            .Should().Be(1);

        await db.MigrateToAsync("20261021072625_Phase5_ReviewsAndMedia");

        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'ProductVariant'"))
            .Should().Be(0);
    }

    // ==================== Phase 8 — backfill وضعیت ====================

    [Fact]
    public async Task Phase8_BackfillsPaymentStateFromLegacyStatus()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        // اسکیمای «قبل از فاز ۸»: فقط Status وجود دارد.
        await db.MigrateToAsync("20261021075900_Phase6_VariantsAndAttributes");

        await db.ExecuteAsync(SeedInvoiceWithPaymentsSql(
            (0, "LEGACY-PENDING"),
            (1, "LEGACY-COMPLETED"),
            (2, "LEGACY-FAILED"),
            (3, "LEGACY-REFUNDED")));

        await db.MigrateToAsync("20261021083534_Phase8_PaymentAndDeliveredAt");

        // نگاشت باید Status → State باشد، نه پیش‌فرض 0.
        (await db.ScalarAsync<int>("SELECT [State] FROM [Payment] WHERE [TransactionId] = 'LEGACY-COMPLETED'"))
            .Should().Be(2, "Completed باید Succeeded شود");

        (await db.ScalarAsync<int>("SELECT [State] FROM [Payment] WHERE [TransactionId] = 'LEGACY-FAILED'"))
            .Should().Be(3);

        (await db.ScalarAsync<int>("SELECT [State] FROM [Payment] WHERE [TransactionId] = 'LEGACY-REFUNDED'"))
            .Should().Be(5);

        (await db.ScalarAsync<int>("SELECT [State] FROM [Payment] WHERE [TransactionId] = 'LEGACY-PENDING'"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Phase8_BackfillsPaymentOrderIdFromInvoice()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateToAsync("20261021075900_Phase6_VariantsAndAttributes");
        await db.ExecuteAsync(SeedInvoiceWithPaymentsSql((1, "LEGACY-ORDER")));

        await db.MigrateToAsync("20261021083534_Phase8_PaymentAndDeliveredAt");

        var orderId = await db.ScalarAsync<int>("SELECT TOP 1 [OrderId] FROM [Order]");

        (await db.ScalarAsync<int>("SELECT [OrderId] FROM [Payment] WHERE [TransactionId] = 'LEGACY-ORDER'"))
            .Should().Be(orderId, "OrderId باید از Invoice پر شود، وگرنه گزارش مالی بی‌سفارش است");
    }

    [Fact]
    public async Task Phase8_BackfillsLastStateChangedTimestamp()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateToAsync("20261021075900_Phase6_VariantsAndAttributes");
        await db.ExecuteAsync(SeedInvoiceWithPaymentsSql((1, "LEGACY-TIME")));

        await db.MigrateToAsync("20261021083534_Phase8_PaymentAndDeliveredAt");

        // مقدار پیش‌فرض DateTime.MinValue یعنی پرداخت «هزاران سال پیش» تغییر کرده
        // و job رهاشده‌ها فوراً آن را می‌بندد.
        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [Payment] WHERE [TransactionId] = 'LEGACY-TIME' AND [LastStateChangedAtUtc] > '2000-01-01'"))
            .Should().Be(1);
    }

    // ==================== Phase 9 — حذف Status ====================

    [Fact]
    public async Task Phase9_DropStatus_RollbackRestoresMeaning()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        // ابتدا روی اسکیمای فاز ۸ (که هنوز Status دارد) داده می‌کاریم،
        // بعد مایگریشن حذف Status را اعمال می‌کنیم.
        await db.MigrateToAsync("20261021083534_Phase8_PaymentAndDeliveredAt");
        await db.ExecuteAsync(SeedInvoiceWithPaymentsSql((1, "P9-ROLLBACK")));

        await db.MigrateAllAsync();

        // در اسکیمای فعلی، State منبع حقیقت است.
        await db.ExecuteAsync("UPDATE [Payment] SET [State] = 2 WHERE [TransactionId] = 'P9-ROLLBACK'");

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE name = 'Status' AND object_id = OBJECT_ID('Payment')"))
            .Should().Be(0, "R39: Status باید حذف شده باشد");

        await db.MigrateToAsync("20261021083534_Phase8_PaymentAndDeliveredAt");

        (await db.ScalarAsync<int>("SELECT [Status] FROM [Payment] WHERE [TransactionId] = 'P9-ROLLBACK'"))
            .Should().Be(1, "Succeeded باید به Completed برگردد، نه پیش‌فرض 0");
    }

    // ==================== Phase 9 — کش امتیاز ====================

    [Fact]
    public async Task Phase9_RatingCache_BackfillsApprovedSellerReviews()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateToAsync("20261021083534_Phase8_PaymentAndDeliveredAt");

        // ایندکس یکتا روی (SellerId, CustomerId) است، پس هر نظر کاربر خودش را دارد.
        await db.ExecuteAsync(@"
INSERT INTO [User] ([FirstName],[LastName],[PasswordHash],[UserType],[IsActive],[PhoneNumber],[SecurityStamp],[FailedLoginAttempts],[CreatedOn],[ModifiedOn])
VALUES ('R','S','h',1,1,'09120000071','s',0,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @sellerUser INT = SCOPE_IDENTITY();

INSERT INTO [Seller] ([UserId],[StoreName],[Status],[IsActive],[CreatedOn],[ModifiedOn])
VALUES (@sellerUser,'RatingShop',1,1,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @s INT = SCOPE_IDENTITY();

DECLARE @cust TABLE (Id INT);
DECLARE @n INT = 1;
WHILE @n <= 3
BEGIN
    INSERT INTO [User] ([FirstName],[LastName],[PasswordHash],[UserType],[IsActive],[PhoneNumber],[SecurityStamp],[FailedLoginAttempts],[CreatedOn],[ModifiedOn])
    VALUES ('C','R','h',0,1,'0913000000' + CAST(@n AS varchar(1)),'s',0,SYSUTCDATETIME(),SYSUTCDATETIME());
    DECLARE @cu INT = SCOPE_IDENTITY();

    INSERT INTO [Customer] ([Points],[LoyaltyLevel],[TotalSpent],[OrderCount],[IsBlocked],[FailedLoginAttempts],[UserId],[CreatedOn],[ModifiedOn])
    VALUES (0,0,0,0,0,0,@cu,SYSUTCDATETIME(),SYSUTCDATETIME());
    DECLARE @cr INT = SCOPE_IDENTITY();

    INSERT INTO @cust (Id) VALUES (@cr);
    SET @n = @n + 1;
END

INSERT INTO [SellerReview] ([SellerId],[CustomerId],[Rating],[Comment],[IsApproved],[CreatedOn],[ModifiedOn])
SELECT @s, Id, 5, 'a', 1, SYSUTCDATETIME(), SYSUTCDATETIME() FROM @cust WHERE Id = (SELECT MIN(Id) FROM @cust);

INSERT INTO [SellerReview] ([SellerId],[CustomerId],[Rating],[Comment],[IsApproved],[CreatedOn],[ModifiedOn])
SELECT @s, Id, 3, 'b', 1, SYSUTCDATETIME(), SYSUTCDATETIME() FROM @cust WHERE Id = (SELECT MAX(Id) FROM @cust WHERE Id < (SELECT MAX(Id) FROM @cust));

INSERT INTO [SellerReview] ([SellerId],[CustomerId],[Rating],[Comment],[IsApproved],[CreatedOn],[ModifiedOn])
SELECT @s, Id, 1, 'c', 0, SYSUTCDATETIME(), SYSUTCDATETIME() FROM @cust WHERE Id = (SELECT MAX(Id) FROM @cust);
");

        await db.MigrateAllAsync();

        var count = await db.ScalarAsync<int>("SELECT [ReviewCount] FROM [Seller] WHERE [StoreName] = 'RatingShop'");
        var avg = await db.ScalarAsync<decimal>("SELECT [AverageRating] FROM [Seller] WHERE [StoreName] = 'RatingShop'");

        count.Should().Be(2, "نظر تأییدنشده نباید شمرده شود");
        avg.Should().Be(4m, "میانگین ۵ و ۳ برابر ۴ است");
    }

    [Fact]
    public async Task Phase9_RatingCache_ProductColumnsExistAndDefaultToZero()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateAllAsync();

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE name IN ('AverageRating','ReviewCount') AND object_id = OBJECT_ID('Product')"))
            .Should().Be(2);

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE name IN ('AverageRating','ReviewCount') AND object_id = OBJECT_ID('Seller')"))
            .Should().Be(2);
    }

    // ==================== Batch 41 / 42 ====================

    private const string Phase14Migration = "20261103131218_Phase14_M713_ProductionDefensibility";
    private const string Batch41Migration = "20261114051734_Phase15_Batch41_RefundWorkflow";
    private const string Batch42Migration = "20261114054648_Phase15_Batch42_PaymentReview";

    [Fact]
    public async Task Batch41_RefundWorkflow_BackfillsRefundedAndVerifyStarted_AndRollbackMapsRefundPendingToSucceeded()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateToAsync(Phase14Migration);

        // State: 1 = Pending، 2 = Succeeded، 5 = Refunded، 6 = RefundPending (فقط از Batch 41).
        await db.ExecuteAsync(SeedPaymentsSql(
            ("B41-REFUNDED", 5, "2026-11-02T08:30:00"),
            ("B41-PENDING-AUDITED", 1, "2026-11-01T09:00:00"),
            ("B41-PENDING-PLAIN", 1, "2026-11-01T09:00:00"),
            ("B41-SUCCEEDED", 2, "2026-11-01T09:00:00")));

        // دو رکورد «تأیید آغاز شد» با زمان‌های متفاوت؛ دیرتری عمداً اول درج می‌شود تا MIN سنجیده شود نه ترتیب درج.
        await db.ExecuteAsync(
            AuditSql("B41-PENDING-AUDITED", "Payment.VerificationStarted", "2026-11-01T10:05:00", "second") +
            AuditSql("B41-PENDING-AUDITED", "Payment.VerificationStarted", "2026-11-01T10:00:00", "first") +
            // پرداخت Succeeded هم رکورد دارد؛ back-fill فقط State = 1 را پر می‌کند.
            AuditSql("B41-SUCCEEDED", "Payment.VerificationStarted", "2026-11-01T09:30:00", "verified"));

        await db.MigrateToAsync(Batch41Migration);

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [Payment] WHERE [TransactionId] = 'B41-REFUNDED' " +
            "AND [RefundedAtUtc] = [LastStateChangedAtUtc]"))
            .Should().Be(1, "Refunded موجود: لحظهٔ برگشت = آخرین تغییر وضعیت");
        (await db.ScalarAsync<DateTime>(
            "SELECT [RefundedAtUtc] FROM [Payment] WHERE [TransactionId] = 'B41-REFUNDED'"))
            .Should().Be(new DateTime(2026, 11, 2, 8, 30, 0));

        (await db.ScalarAsync<DateTime>(
            "SELECT [VerifyStartedAtUtc] FROM [Payment] WHERE [TransactionId] = 'B41-PENDING-AUDITED'"))
            .Should().Be(new DateTime(2026, 11, 1, 10, 0, 0), "اولین رکورد Payment.VerificationStarted");

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [Payment] WHERE [TransactionId] IN ('B41-PENDING-PLAIN','B41-SUCCEEDED','B41-REFUNDED') " +
            "AND [VerifyStartedAtUtc] IS NULL"))
            .Should().Be(3, "بدون رکورد ممیزی یا غیر Pending → NULL");

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [Payment] WHERE [TransactionId] <> 'B41-REFUNDED' AND [RefundedAtUtc] IS NULL"))
            .Should().Be(3, "فقط پرداخت Refunded لحظهٔ برگشت می‌گیرد");

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [Payment] WHERE [RefundReason] IS NOT NULL OR [RefundReference] IS NOT NULL " +
            "OR [RefundRequestedAtUtc] IS NOT NULL"))
            .Should().Be(0);

        // RefundPending فقط در کد جدید وجود دارد.
        await db.ExecuteAsync("UPDATE [Payment] SET [State] = 6 WHERE [TransactionId] = 'B41-SUCCEEDED'");

        await db.MigrateToAsync(Phase14Migration);

        (await db.ScalarAsync<int>("SELECT [State] FROM [Payment] WHERE [TransactionId] = 'B41-SUCCEEDED'"))
            .Should().Be(2, "RefundPending در Down به Succeeded برمی‌گردد (پول هنوز برنگشته)، نه Refunded");
        (await db.ScalarAsync<int>("SELECT [State] FROM [Payment] WHERE [TransactionId] = 'B41-REFUNDED'"))
            .Should().Be(5);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM [Payment] WHERE [State] = 6"))
            .Should().Be(0);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM [Payment]"))
            .Should().Be(4, "rollback نباید پرداختی را حذف کند");

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name IN " +
            "('RefundReason','RefundReference','RefundRequestedAtUtc','RefundedAtUtc','VerifyStartedAtUtc')"))
            .Should().Be(0, "Down ستون‌های Batch 41 را برمی‌دارد");
    }

    [Fact]
    public async Task Batch42_PaymentReview_BackfillsFromAuditLog_AndRollbackDropsColumns()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateToAsync(Batch41Migration);

        await db.ExecuteAsync(SeedPaymentsSql(
            ("B42-REVIEW", 2, "2026-11-03T08:00:00"),
            ("B42-REVIEW-LONG", 2, "2026-11-03T08:00:00"),
            ("B42-PLAIN", 2, "2026-11-03T08:00:00")));

        var longDetails = new string('x', 1500);

        await db.ExecuteAsync(
            // دیرترین شرح اول درج می‌شود: ReviewNote باید «آخرین» بر اساس زمان باشد، نه ترتیب درج.
            AuditSql("B42-REVIEW", "Payment.ReviewRequired", "2026-11-03T11:00:00", "latest details") +
            AuditSql("B42-REVIEW", "Payment.ReviewRequired", "2026-11-03T09:00:00", "first details") +
            AuditSql("B42-REVIEW-LONG", "Payment.ReviewRequired", "2026-11-03T08:15:00", "short old") +
            AuditSql("B42-REVIEW-LONG", "Payment.ReviewRequired", "2026-11-03T12:00:00", longDetails) +
            // رکورد ممیزی دیگری برای همان پرداخت؛ نباید در back-fill دیده شود.
            AuditSql("B42-PLAIN", "Payment.VerificationStarted", "2026-11-03T08:05:00", "not a review"));

        await db.MigrateToAsync(Batch42Migration);

        (await db.ScalarAsync<DateTime>(
            "SELECT [ReviewRequiredAtUtc] FROM [Payment] WHERE [TransactionId] = 'B42-REVIEW'"))
            .Should().Be(new DateTime(2026, 11, 3, 9, 0, 0), "اولین زمان Payment.ReviewRequired");
        (await db.ScalarAsync<string>(
            "SELECT [ReviewNote] FROM [Payment] WHERE [TransactionId] = 'B42-REVIEW'"))
            .Should().Be("latest details", "آخرین شرح Payment.ReviewRequired");

        (await db.ScalarAsync<DateTime>(
            "SELECT [ReviewRequiredAtUtc] FROM [Payment] WHERE [TransactionId] = 'B42-REVIEW-LONG'"))
            .Should().Be(new DateTime(2026, 11, 3, 8, 15, 0));
        (await db.ScalarAsync<string>(
            "SELECT [ReviewNote] FROM [Payment] WHERE [TransactionId] = 'B42-REVIEW-LONG'"))
            .Should().Be(new string('x', 1000), "شرح بلندتر از ۱۰۰۰ نویسه با LEFT(…, 1000) کوتاه می‌شود");

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [Payment] WHERE [TransactionId] = 'B42-PLAIN' " +
            "AND [ReviewRequiredAtUtc] IS NULL AND [ReviewNote] IS NULL"))
            .Should().Be(1, "بدون رکورد Payment.ReviewRequired → NULL");

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [Payment] WHERE [ReviewResolvedAtUtc] IS NOT NULL OR [ReviewResolvedByUserId] IS NOT NULL"))
            .Should().Be(0, "بستن مورد بررسی با ادمین است، نه با مایگریشن");

        await db.MigrateToAsync(Batch41Migration);

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('Payment') AND name IN " +
            "('ReviewNote','ReviewRequiredAtUtc','ReviewResolvedAtUtc','ReviewResolvedByUserId')"))
            .Should().Be(0, "Down ستون‌های Batch 42 را برمی‌دارد");
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM [Payment]"))
            .Should().Be(3, "rollback نباید پرداختی را حذف کند");
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM [Payment] WHERE [State] = 2"))
            .Should().Be(3, "Down در Batch 42 وضعیت را تغییر نمی‌دهد");
    }

    /// <summary>
    /// زنجیرهٔ User → Customer → Order → Invoice روی اسکیمای Phase14 (یا بعدتر: ستون‌های تازهٔ Payment همه nullable‌اند)
    /// و برای هر ورودی یک Payment با State و LastStateChangedAtUtc داده‌شده. RowVersion را SQL Server پر می‌کند.
    /// </summary>
    private static string SeedPaymentsSql(params (string TransactionId, int State, string LastStateChangedAtUtc)[] payments)
    {
        var values = string.Join(",\n",
            payments.Select(p =>
                $"(100, SYSUTCDATETIME(), 0, 0, {p.State}, '{p.LastStateChangedAtUtc}', 1, @i, @o, '{p.TransactionId}', SYSUTCDATETIME(), SYSUTCDATETIME())"));

        return $@"
INSERT INTO [User] ([FirstName],[LastName],[PasswordHash],[UserType],[IsActive],[PhoneNumber],[SecurityStamp],[FailedLoginAttempts],[CreatedOn],[ModifiedOn])
VALUES ('M','T','h',1,1,'09120004141','s',0,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @u INT = SCOPE_IDENTITY();

INSERT INTO [Customer] ([Points],[LoyaltyLevel],[TotalSpent],[OrderCount],[UserId],[CreatedOn],[ModifiedOn])
VALUES (0,0,0,0,@u,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @c INT = SCOPE_IDENTITY();

INSERT INTO [Order] ([OrderDate],[TotalAmount],[DiscountAmount],[ShippingCost],[CouponQuotaRefunded],[Status],[ShippingFullName],[ShippingAddress],[ShippingPhoneNumber],[CustomerId],[CreatedOn],[ModifiedOn])
VALUES (SYSUTCDATETIME(),100,0,0,0,0,'M','Addr','09120004141',@c,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @o INT = SCOPE_IDENTITY();

INSERT INTO [Invoice] ([OrderId],[InvoiceNumber],[InvoiceDate],[SubTotalAmount],[TaxAmount],[DiscountAmount],[TotalAmount],[Status],[CreatedOn],[ModifiedOn])
VALUES (@o,'INV-{Guid.NewGuid():N}',SYSUTCDATETIME(),100,0,0,100,0,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @i INT = SCOPE_IDENTITY();

INSERT INTO [Payment] ([Amount],[PaymentDate],[PaymentMethod],[Gateway],[State],[LastStateChangedAtUtc],[IsActive],[InvoiceId],[OrderId],[TransactionId],[CreatedOn],[ModifiedOn])
VALUES {values};
";
    }

    /// <summary>یک رکورد AuditLog برای پرداختِ دارای این TransactionId (EntityId = PaymentId به‌صورت متن، مانند AuditService).</summary>
    private static string AuditSql(string transactionId, string action, string occurredOnUtc, string details) => $@"
INSERT INTO [AuditLog] ([Action],[EntityName],[EntityId],[Details],[OccurredOnUtc],[UserId],[IpAddress])
SELECT N'{action}', N'Payment', CAST([PaymentId] AS nvarchar(20)), N'{details.Replace("'", "''")}', '{occurredOnUtc}', NULL, NULL
FROM [Payment] WHERE [TransactionId] = '{transactionId}';
";

    // ==================== ابزار ====================

    /// <summary>
    /// یک زنجیرهٔ User → Customer → Order → Invoice می‌سازد و برای هر ورودی
    /// یک Payment با Status قدیمی درج می‌کند.
    /// </summary>
    private static string SeedInvoiceWithPaymentsSql(params (int Status, string TransactionId)[] payments)
    {
        var values = string.Join(",\n",
            payments.Select(p =>
                $"({100}, SYSUTCDATETIME(), 0, {p.Status}, 1, @i, '{p.TransactionId}', SYSUTCDATETIME(), SYSUTCDATETIME())"));

        return $@"
INSERT INTO [User] ([FirstName],[LastName],[PasswordHash],[UserType],[IsActive],[PhoneNumber],[SecurityStamp],[FailedLoginAttempts],[CreatedOn],[ModifiedOn])
VALUES ('M','T','h',0,1,'09120000001','s',0,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @u INT = SCOPE_IDENTITY();

INSERT INTO [Customer] ([Points],[LoyaltyLevel],[TotalSpent],[OrderCount],[IsBlocked],[FailedLoginAttempts],[UserId],[CreatedOn],[ModifiedOn])
VALUES (0,0,0,0,0,0,@u,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @c INT = SCOPE_IDENTITY();

INSERT INTO [Order] ([OrderDate],[TotalAmount],[Status],[ShippingFullName],[ShippingAddress],[ShippingPhoneNumber],[CustomerId],[CreatedOn],[ModifiedOn])
VALUES (SYSUTCDATETIME(),100,0,'M','Addr','0912',@c,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @o INT = SCOPE_IDENTITY();

INSERT INTO [Invoice] ([OrderId],[InvoiceNumber],[InvoiceDate],[SubTotalAmount],[TaxAmount],[DiscountAmount],[TotalAmount],[Status],[CreatedOn],[ModifiedOn])
VALUES (@o,'INV-{Guid.NewGuid():N}',SYSUTCDATETIME(),100,0,0,100,0,SYSUTCDATETIME(),SYSUTCDATETIME());
DECLARE @i INT = SCOPE_IDENTITY();

INSERT INTO [Payment] ([Amount],[PaymentDate],[PaymentMethod],[Status],[IsActive],[InvoiceId],[TransactionId],[CreatedOn],[ModifiedOn])
VALUES {values};
";
    }

    // ==================== Batch 43 ====================

    /// <summary>
    /// Batch 43 (FIN-07) — <c>Phase15_Batch43_PaymentGatewayDetails</c>: سه ستون اختیاری روی Payment (کارمزد درگاه،
    /// پرداخت‌کنندهٔ کارمزد، کارت ماسک‌شده)، بدون back-fill. بالا: ستون‌ها با نوع و nullable درست ساخته می‌شوند؛
    /// پایین (تا Batch 42): هر سه برداشته می‌شوند؛ دوباره بالا: دوباره ساخته می‌شوند.
    /// </summary>
    [Fact]
    public async Task Phase15_Batch43_PaymentGatewayDetails_AppliesAndRollsBack()
    {
        const string batch43 = "20261114134136_Phase15_Batch43_PaymentGatewayDetails";
        const string batch42 = "20261114054648_Phase15_Batch42_PaymentReview";
        const string gatewayColumns =
            "SELECT COUNT(*) FROM sys.columns " +
            "WHERE name IN ('GatewayFee','GatewayFeeType','CardPanMasked') AND object_id = OBJECT_ID('Payment')";

        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateToAsync(batch42);

        (await db.ScalarAsync<int>(gatewayColumns)).Should().Be(0, "پیش از Batch 43 این ستون‌ها وجود ندارند");

        await db.MigrateToAsync(batch43);

        (await db.ScalarAsync<int>(gatewayColumns)).Should().Be(3, "Batch 43 هر سه ستون را می‌سازد");

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id " +
            "WHERE c.object_id = OBJECT_ID('Payment') AND c.name = 'GatewayFee' " +
            "AND t.name = 'decimal' AND c.precision = 18 AND c.scale = 0 AND c.is_nullable = 1"))
            .Should().Be(1, "GatewayFee باید decimal(18,0) NULL باشد");

        // nvarchar(20) در sys.columns طول ۴۰ بایت دارد.
        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id " +
            "WHERE c.object_id = OBJECT_ID('Payment') AND c.name IN ('GatewayFeeType','CardPanMasked') " +
            "AND t.name = 'nvarchar' AND c.max_length = 40 AND c.is_nullable = 1"))
            .Should().Be(2, "GatewayFeeType و CardPanMasked باید nvarchar(20) NULL باشند");

        await db.MigrateToAsync(batch42);

        (await db.ScalarAsync<int>(gatewayColumns)).Should().Be(0, "rollback باید هر سه ستون را بردارد");

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE name = 'ReviewRequiredAtUtc' AND object_id = OBJECT_ID('Payment')"))
            .Should().Be(1, "rollback نباید به ستون‌های Batch 42 دست بزند");

        await db.MigrateToAsync(batch43);

        (await db.ScalarAsync<int>(gatewayColumns)).Should().Be(3, "اعمال دوباره پس از rollback باید ممکن باشد");
    }

    // ==================== Batch 44 ====================

    private const string Batch43Migration = "20261114134136_Phase15_Batch43_PaymentGatewayDetails";
    private const string Batch44Migration = "20261115133045_Phase15_Batch44_PayoutHoldAndNet";

    /// <summary>
    /// Batch 44 — <c>Phase15_Batch44_PayoutHoldAndNet</c>. بالا: تسویهٔ بسته از تحویل + ۷ روز و تسویهٔ دستی از PeriodEnd قابل پرداخت؛
    /// خالص = مبلغ و کسر = ۰؛ Amount بدهی به OriginalAmount تغییر نام می‌دهد و بدهی تسویه‌شده مبلغ تسویه‌شده و زمان می‌گیرد؛
    /// فقط حساب غیرفعالِ مالک نشانی انبار پلتفرم (بدون نقش و مشتری/کارمند/فروشنده) IsSystem می‌شود؛ انبار دوم پلتفرم رد می‌شود.
    /// پایین: ستون‌ها برداشته می‌شوند و Amount بدهی تسویه‌نشده دوباره «باقی‌مانده» است. با دو انبار پلتفرم مهاجرت متوقف می‌شود.
    /// </summary>
    [Fact]
    public async Task Batch44_PayoutHoldAndNet_BackfillsAndRollsBack()
    {
        await using var db = await MigrationTestDatabase.CreateAsync(_server.MasterConnectionString);

        await db.MigrateToAsync(Batch43Migration);
        await db.ExecuteAsync(Batch44SeedSql);

        await db.MigrateToAsync(Batch44Migration);

        (await db.ScalarAsync<DateTime>("SELECT [AvailableAtUtc] FROM [SellerPayout] WHERE [OrderSellerGroupId] IS NOT NULL"))
            .Should().Be(new DateTime(2026, 11, 8, 10, 0, 0), "تحویل ۲۰۲۶-۱۱-۰۱ ۱۰:۰۰ + ۷ روز");
        (await db.ScalarAsync<DateTime>("SELECT [AvailableAtUtc] FROM [SellerPayout] WHERE [OrderSellerGroupId] IS NULL"))
            .Should().Be(new DateTime(2026, 10, 31), "تسویهٔ دستی بدون تحویل: PeriodEnd");
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM [SellerPayout] WHERE [NetAmount] = [Amount] AND [DebtOffsetAmount] = 0"))
            .Should().Be(2);

        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [SellerDebt] WHERE [Reason] = 'B44-OPEN' AND [OriginalAmount] = 300 AND [SettledAmount] = 0 AND [SettledAtUtc] IS NULL"))
            .Should().Be(1);
        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [SellerDebt] WHERE [Reason] = 'B44-SETTLED' AND [OriginalAmount] = 200 AND [SettledAmount] = 200 " +
            "AND [SettledAtUtc] = '2026-11-02T00:00:00'"))
            .Should().Be(1, "بدهی تسویه‌شده: تسویه‌شده = مبلغ، زمان = ModifiedOn");
        (await db.ScalarAsync<string>(
                "SELECT STRING_AGG(d.[Reason], ',') FROM [AuditLog] a JOIN [SellerDebt] d ON a.[EntityId] = CAST(d.[SellerDebtId] AS nvarchar(20)) " +
                "WHERE a.[Action] = 'SellerDebt.MigrationReview' AND a.[EntityName] = 'SellerDebt'"))
            .Should().Be("B44-PARTLY", "بدهی بازی که پس از ساخت تغییر کرده (کسر جزئی کد قدیمی) برای بررسی دستی ثبت می‌شود");
        (await db.ScalarAsync<int>(
                "SELECT COUNT(*) FROM [SellerDebt] WHERE [Reason] = 'B44-PARTLY' AND [OriginalAmount] = 150 AND [SettledAmount] = 0"))
            .Should().Be(1, "مبلغ اصلی قابل بازسازی نیست؛ باقی‌ماندهٔ فعلی نگه داشته می‌شود");

        (await db.ScalarAsync<string>("SELECT STRING_AGG([FirstName], ',') FROM [User] WHERE [IsSystem] = 1"))
            .Should().Be("System", "فقط حساب غیرفعالِ مالک نشانی انبار پلتفرم");
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM [Warehouse] WHERE [IsPlatform] = 1 AND [SellerId] IS NULL")).Should().Be(1);

        var secondPlatform = () => db.ExecuteAsync(SecondPlatformWarehouseSql);
        (await secondPlatform.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>())
            .Which.Number.Should().Be(2601, "UX_Warehouse_SinglePlatform");

        // پس از کسر جزئی، Down باید Amount را «باقی‌مانده» کند (معنای کد قدیمی).
        await db.ExecuteAsync("UPDATE [SellerDebt] SET [SettledAmount] = 100 WHERE [Reason] = 'B44-OPEN'");

        await db.MigrateToAsync(Batch43Migration);

        (await db.ScalarAsync<decimal>("SELECT [Amount] FROM [SellerDebt] WHERE [Reason] = 'B44-OPEN'")).Should().Be(200m);
        (await db.ScalarAsync<decimal>("SELECT [Amount] FROM [SellerDebt] WHERE [Reason] = 'B44-SETTLED'")).Should().Be(200m);
        (await db.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE " +
            "(object_id = OBJECT_ID('SellerPayout') AND name IN ('AvailableAtUtc','DebtOffsetAmount','NetAmount')) OR " +
            "(object_id = OBJECT_ID('SellerDebt') AND name IN ('OriginalAmount','SettledAmount','SettledAtUtc')) OR " +
            "(object_id = OBJECT_ID('User') AND name = 'IsSystem') OR (object_id = OBJECT_ID('Warehouse') AND name = 'IsPlatform')"))
            .Should().Be(0, "Down همهٔ ستون‌های Batch 44 را برمی‌دارد");
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM [SellerPayout]")).Should().Be(2, "rollback نباید تسویه‌ای را حذف کند");

        // دو انبار پلتفرم پیش از مهاجرت: مهاجرت با پیام روشن متوقف می‌شود و چیزی تغییر نمی‌کند.
        await db.ExecuteAsync(SecondPlatformWarehouseSql);
        var blocked = () => db.MigrateToAsync(Batch44Migration);
        await blocked.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>().WithMessage("*more than one platform warehouse*");
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('SellerDebt') AND name = 'Amount'"))
            .Should().Be(1, "مهاجرت ناموفق در تراکنش برمی‌گردد");

        await db.ExecuteAsync("DELETE FROM [Warehouse] WHERE [Name] = N'B44-Second'");
        await db.MigrateToAsync(Batch44Migration);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_Warehouse_SinglePlatform'")).Should().Be(1);
        (await db.ScalarAsync<int>("SELECT COUNT(*) FROM [AuditLog] WHERE [Action] = 'SellerDebt.MigrationReview'"))
            .Should().Be(1, "اجرای دوباره رکورد بررسی تکراری نمی‌سازد");
    }

    private const string SecondPlatformWarehouseSql =
        "INSERT INTO [Warehouse] ([Name],[AddressId],[SellerId],[CreatedOn],[ModifiedOn]) " +
        "SELECT N'B44-Second', [AddressId], NULL, SYSUTCDATETIME(), SYSUTCDATETIME() FROM [Warehouse] WHERE [Name] = N'B44-Platform'";

    /// <summary>
    /// اسکیمای Batch 43: فروشنده، مشتری، حساب سیستمی غیرفعال (مالک نشانی انبار پلتفرم)، کاربر غیرفعال عادی، انبار پلتفرم و انبار
    /// فروشنده، سفارش با بستهٔ تحویل‌شده (۲۰۲۶-۱۱-۰۱ ۱۰:۰۰) و تسویهٔ Pending آن، تسویهٔ دستی Paid، بدهی باز ۳۰۰، تسویه‌شدهٔ ۲۰۰ و بدهی باز ۱۵۰ که پس از ساخت تغییر کرده (کسر جزئی کد قدیمی).    /// </summary>
    private const string Batch44SeedSql = @"
DECLARE @now datetime2 = SYSUTCDATETIME();
INSERT INTO [User] (FirstName,LastName,PasswordHash,UserType,IsActive,PhoneNumber,SecurityStamp,FailedLoginAttempts,CreatedOn,ModifiedOn) VALUES ('Seller','B44','h',2,1,'09124400001','s',0,@now,@now);
DECLARE @u1 int = SCOPE_IDENTITY();
INSERT INTO [User] (FirstName,LastName,PasswordHash,UserType,IsActive,PhoneNumber,SecurityStamp,FailedLoginAttempts,CreatedOn,ModifiedOn) VALUES ('Buyer','B44','h',1,1,'09124400002','s',0,@now,@now);
DECLARE @u2 int = SCOPE_IDENTITY();
INSERT INTO [User] (FirstName,LastName,PasswordHash,UserType,IsActive,PhoneNumber,SecurityStamp,FailedLoginAttempts,CreatedOn,ModifiedOn) VALUES ('System','B44','h',2,0,'09124400003','s',0,@now,@now);
DECLARE @u3 int = SCOPE_IDENTITY();
INSERT INTO [User] (FirstName,LastName,PasswordHash,UserType,IsActive,PhoneNumber,SecurityStamp,FailedLoginAttempts,CreatedOn,ModifiedOn) VALUES ('Inactive','B44','h',1,0,'09124400004','s',0,@now,@now);
DECLARE @u4 int = SCOPE_IDENTITY();
INSERT INTO [Province] (ProvinceName,CreatedOn,ModifiedOn) VALUES (N'B44-P',@now,@now);
DECLARE @p int = SCOPE_IDENTITY();
INSERT INTO [City] (CityName,ProvinceId,CreatedOn,ModifiedOn) VALUES (N'B44-C',@p,@now,@now);
DECLARE @c int = SCOPE_IDENTITY();
INSERT INTO [Address] (CityId,Plaque,Unit,PostalCode,RecipientFirstName,RecipientLastName,IsDefault,UserId,CreatedOn,ModifiedOn) VALUES (@c,'1','1','1234567890','A','B',0,@u3,@now,@now);
DECLARE @a1 int = SCOPE_IDENTITY();
INSERT INTO [Address] (CityId,Plaque,Unit,PostalCode,RecipientFirstName,RecipientLastName,IsDefault,UserId,CreatedOn,ModifiedOn) VALUES (@c,'2','1','1234567890','A','B',0,@u4,@now,@now);
DECLARE @a2 int = SCOPE_IDENTITY();
INSERT INTO [Seller] (UserId,StoreName,Status,IsActive,CreatedOn,ModifiedOn) VALUES (@u1,N'B44 Store',1,1,@now,@now);
DECLARE @s int = SCOPE_IDENTITY();
INSERT INTO [Customer] (Points,LoyaltyLevel,TotalSpent,OrderCount,UserId,CreatedOn,ModifiedOn) VALUES (0,0,0,0,@u2,@now,@now);
DECLARE @cu int = SCOPE_IDENTITY();
INSERT INTO [Customer] (Points,LoyaltyLevel,TotalSpent,OrderCount,UserId,CreatedOn,ModifiedOn) VALUES (0,0,0,0,@u4,@now,@now);
INSERT INTO [Warehouse] (Name,AddressId,SellerId,CreatedOn,ModifiedOn) VALUES (N'B44-Platform',@a1,NULL,@now,@now);
INSERT INTO [Warehouse] (Name,AddressId,SellerId,CreatedOn,ModifiedOn) VALUES (N'B44-SellerWh',@a2,@s,@now,@now);
INSERT INTO [Order] (OrderDate,TotalAmount,Status,ShippingFullName,ShippingAddress,ShippingPhoneNumber,CustomerId,CouponQuotaRefunded,CreatedOn,ModifiedOn) VALUES (@now,1100,3,'B','Addr','09124400002',@cu,0,@now,@now);
DECLARE @o int = SCOPE_IDENTITY();
INSERT INTO [OrderSellerGroup] (OrderId,SellerId,SubTotal,CommissionRate,CommissionAmount,SellerPayable,Status,DeliveredAt,CreatedOn,ModifiedOn) VALUES (@o,@s,1100,10,110,1000,3,'2026-11-01T10:00:00',@now,@now);
DECLARE @g int = SCOPE_IDENTITY();
INSERT INTO [SellerPayout] (SellerId,OrderSellerGroupId,Amount,Status,PeriodStart,PeriodEnd,CreatedOn,ModifiedOn) VALUES (@s,@g,1000,0,'2026-10-30','2026-11-01T10:00:00',@now,@now);
INSERT INTO [SellerPayout] (SellerId,OrderSellerGroupId,Amount,Status,PeriodStart,PeriodEnd,Reference,PaidAt,CreatedOn,ModifiedOn) VALUES (@s,NULL,500,1,'2026-10-01','2026-10-31','MANUAL','2026-10-31',@now,@now);
INSERT INTO [SellerDebt] (SellerId,Amount,Reason,CreatedAtUtc,IsSettled,CreatedOn,ModifiedOn) VALUES (@s,300,'B44-OPEN','2026-11-01',0,@now,@now);
INSERT INTO [SellerDebt] (SellerId,Amount,Reason,CreatedAtUtc,IsSettled,CreatedOn,ModifiedOn) VALUES (@s,200,'B44-SETTLED','2026-10-01',1,@now,'2026-11-02T00:00:00');
INSERT INTO [SellerDebt] (SellerId,Amount,Reason,CreatedAtUtc,IsSettled,CreatedOn,ModifiedOn) VALUES (@s,150,'B44-PARTLY','2026-10-01',0,'2026-10-01T00:00:00','2026-10-20T00:00:00');
";
}
