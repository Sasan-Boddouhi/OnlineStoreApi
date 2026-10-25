using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;

namespace OnlineStore.Tests.Migrations;

/// <summary>
/// یک کانتینر SQL Server مشترک برای همهٔ تست‌های این مجموعه.
///
/// چرا Testcontainers و نه LocalDB: LocalDB فقط ویندوز است و روی runner
/// لینوکسی وجود ندارد. با این کار تست‌های مایگریشن روی هر سیستمی که Docker
/// دارد اجرا می‌شوند (ویندوز، مک، Ubuntu CI) و هیچ نصب دستی لازم نیست.
///
/// کانتینر یک بار برای کل مجموعه بالا می‌آید؛ هر تست دیتابیس خودش را
/// داخل همان کانتینر می‌سازد و در پایان حذف می‌کند.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    /// <summary>
    /// اگر این متغیر تنظیم شود، از یک SQL Server موجود استفاده می‌شود
    /// (مثلاً LocalDB روی ویندوز) و کانتینر بالا نمی‌آید.
    /// برای محیط‌هایی که Docker ندارند، این تنها راه اجرای تست‌ها است.
    /// </summary>
    public const string ExternalServerVariable = "MIGRATION_TEST_SQL";

    private MsSqlContainer? _container;

    /// <summary>رشتهٔ اتصال برای دیتابیس <c>master</c>.</summary>
    public string MasterConnectionString { get; private set; } = string.Empty;

    public bool UsedContainer => _container is not null;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalServerVariable);

        if (!string.IsNullOrWhiteSpace(external))
        {
            MasterConnectionString = external;
            return;
        }

        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04")
            .WithPassword("Str0ng_Passw0rd_For_Tests!")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilCommandIsCompleted("/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost",
                        "-U", "sa", "-P", "Str0ng_Passw0rd_For_Tests!", "-C", "-Q", "SELECT 1"))
            .Build();

        await _container.StartAsync();

        MasterConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}