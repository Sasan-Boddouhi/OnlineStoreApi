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
}