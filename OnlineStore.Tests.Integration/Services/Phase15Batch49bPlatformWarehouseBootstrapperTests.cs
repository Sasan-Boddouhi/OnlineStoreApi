using Application.Entities;
using Application.Options;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Online_Store_Application.Services;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 49b (پاسخ مالک به Batch 49): انبار پلتفرم هنگام راه‌اندازی از پیکربندی <c>PlatformWarehouse:*</c>
/// ساخته می‌شود (idempotent). در Production، نبودِ انبار و نبودِ پیکربندی راه‌اندازی را متوقف می‌کند؛
/// بیرون از Production فقط هشدار ثبت می‌شود و 409 <c>PLATFORM_WAREHOUSE_NOT_CONFIGURED</c> تور ایمنی می‌ماند.
/// </summary>
public class Phase15Batch49bPlatformWarehouseBootstrapperTests : BaseIntegrationTest
{
    public Phase15Batch49bPlatformWarehouseBootstrapperTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task Bootstrapper_WhenNoPlatformWarehouse_CreatesExactlyOne_AndIsIdempotent()
    {
        await RemovePlatformWarehousesAsync();
        var options = ValidOptions(phone: "+98 912 000 4949");
        var bootstrapper = CreateBootstrapper(options, "Testing");

        var first = await bootstrapper.EnsureAsync(CancellationToken.None);
        var second = await bootstrapper.EnsureAsync(CancellationToken.None);

        first.Should().Be(PlatformWarehouseBootstrapOutcome.Created);
        second.Should().Be(PlatformWarehouseBootstrapOutcome.AlreadyExists);

        var db = Db();
        var platform = await db.Warehouse.AsNoTracking()
            .Include(w => w.Address).ThenInclude(a => a.City).ThenInclude(c => c.Province)
            .Where(w => w.SellerId == null)
            .ToListAsync();

        platform.Should().ContainSingle();
        var warehouse = platform[0];
        warehouse.Name.Should().Be(options.Name);
        warehouse.Address.StreetAddress.Should().Be(options.StreetAddress);
        warehouse.Address.PostalCode.Should().Be(options.PostalCode);
        warehouse.Address.RecipientFirstName.Should().Be(options.RecipientFirstName);
        warehouse.Address.City.CityName.Should().Be(options.CityName);
        warehouse.Address.City.Province.ProvinceName.Should().Be(options.ProvinceName);
        warehouse.Address.ExtraDescription.Should().Contain("09120004949");

        // مالک آدرس: حساب سیستمی غیرفعال با همان شمارهٔ نرمال‌شده (کسی با آن وارد نمی‌شود).
        var owner = await db.User.AsNoTracking().SingleAsync(u => u.UserId == warehouse.Address.UserId);
        owner.PhoneNumber.Should().Be("09120004949");
        owner.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Bootstrapper_PhoneOfExistingUser_UsesThatUserAsOwner_AndReusesExistingGeography()
    {
        await RemovePlatformWarehousesAsync();
        var db = Db();
        var province = new Province { ProvinceName = "B49b-P-" + Guid.NewGuid().ToString("N")[..8] };
        db.Province.Add(province);
        await db.SaveChangesAsync();
        db.City.Add(new City { CityName = "B49b-C", ProvinceId = province.ProvinceId });
        await db.SaveChangesAsync();
        var usersBefore = await db.User.AsNoTracking().CountAsync();
        var provincesBefore = await db.Province.AsNoTracking().CountAsync();
        var citiesBefore = await db.City.AsNoTracking().CountAsync();

        // شمارهٔ ادمین Seed با ارقام فارسی؛ باید نرمال شود و همان کاربر را پیدا کند.
        var options = ValidOptions(phone: "۰۹۱۲۳۴۵۶۷۸۹");
        options.ProvinceName = province.ProvinceName;
        options.CityName = "B49b-C";

        var outcome = await CreateBootstrapper(options, "Testing").EnsureAsync(CancellationToken.None);

        outcome.Should().Be(PlatformWarehouseBootstrapOutcome.Created);
        var fresh = Db();
        var warehouse = await fresh.Warehouse.AsNoTracking().Include(w => w.Address)
            .SingleAsync(w => w.SellerId == null);
        var admin = await fresh.User.AsNoTracking().SingleAsync(u => u.PhoneNumber == "09123456789");
        warehouse.Address.UserId.Should().Be(admin.UserId);
        (await fresh.User.AsNoTracking().CountAsync()).Should().Be(usersBefore);
        (await fresh.Province.AsNoTracking().CountAsync()).Should().Be(provincesBefore);
        (await fresh.City.AsNoTracking().CountAsync()).Should().Be(citiesBefore);
    }

    [Fact]
    public async Task Bootstrapper_WhenPlatformWarehouseExists_DoesNothing_EvenWithoutConfiguration()
    {
        var before = await Db().Warehouse.AsNoTracking().CountAsync();

        var outcome = await CreateBootstrapper(new PlatformWarehouseOptions(), "Production")
            .EnsureAsync(CancellationToken.None);

        outcome.Should().Be(PlatformWarehouseBootstrapOutcome.AlreadyExists);
        (await Db().Warehouse.AsNoTracking().CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Bootstrapper_OutsideProduction_WithoutConfiguration_DoesNotFail_AndCreatesNothing()
    {
        await RemovePlatformWarehousesAsync();

        var outcome = await CreateBootstrapper(new PlatformWarehouseOptions(), "Testing")
            .EnsureAsync(CancellationToken.None);

        outcome.Should().Be(PlatformWarehouseBootstrapOutcome.NotConfigured);
        (await Db().Warehouse.AsNoTracking().AnyAsync(w => w.SellerId == null)).Should().BeFalse();
    }

    [Fact]
    public async Task Bootstrapper_InProduction_WithoutPlatformWarehouseAndWithoutConfiguration_Throws()
    {
        await RemovePlatformWarehousesAsync();

        var act = () => CreateBootstrapper(new PlatformWarehouseOptions(), "Production")
            .EnsureAsync(CancellationToken.None);

        await act.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage("*PlatformWarehouse*");
        (await Db().Warehouse.AsNoTracking().AnyAsync(w => w.SellerId == null)).Should().BeFalse();
    }

    // ==================== ابزار ====================

    private AppDbContext Db() => GetService<AppDbContext>();

    private PlatformWarehouseBootstrapper CreateBootstrapper(PlatformWarehouseOptions options, string environment) =>
        new(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(options),
            new StubHostEnvironment(environment),
            NullLogger<PlatformWarehouseBootstrapper>.Instance);

    private static PlatformWarehouseOptions ValidOptions(string phone) => new()
    {
        Name = "انبار مرکزی B49b-" + Guid.NewGuid().ToString("N")[..6],
        ProvinceName = "تهران B49b-" + Guid.NewGuid().ToString("N")[..6],
        CityName = "تهران",
        StreetAddress = "خیابان آزمایش، پلاک ۱",
        Plaque = "12",
        Unit = "3",
        PostalCode = "1234567890",
        RecipientFirstName = "مسئول",
        RecipientLastName = "انبار",
        PhoneNumber = phone
    };

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "OnlineStore.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private async Task RemovePlatformWarehousesAsync()
    {
        var db = Db();
        db.Warehouse.RemoveRange(await db.Warehouse.Where(w => w.SellerId == null).ToListAsync());
        await db.SaveChangesAsync();
    }
}
