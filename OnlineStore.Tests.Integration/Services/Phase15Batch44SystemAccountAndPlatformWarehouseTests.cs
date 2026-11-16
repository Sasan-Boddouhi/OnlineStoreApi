using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using Application.Interfaces.Security;
using Application.Options;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.User;
using BusinessLogic.DTOs.Warehouse;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Online_Store_Application.Services;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Batch 44 — افزوده‌های مالک از Batch 49b:
///  • حساب سیستمی (<see cref="User.IsSystem"/>) کاملاً بی‌اثر است: ورود با رمز و refresh رد می‌شود، از هیچ API فعال، ویرایش،
///    حذف یا نقش‌دار نمی‌شود (409 USER_IS_SYSTEM) و در فهرست کاربران ادمین با isSystem دیده می‌شود.
///  • حداکثر یک انبار پلتفرم (ایندکس یکتای UX_Warehouse_SinglePlatform)؛ بوت‌استرپرِ دوم که هم‌زمان می‌سازد با نقض ایندکس
///    برمی‌گردد و انبار موجود را دوباره می‌خواند.
/// </summary>
public class Phase15Batch44SystemAccountAndPlatformWarehouseTests : ControllerIntegrationTestBase
{
    private const string Password = "Test@123";

    public Phase15Batch44SystemAccountAndPlatformWarehouseTests(IntegrationTestFactory<Program> factory) : base(factory)
    {
        Factory.Clock.SetUtcNow(DateTimeOffset.UtcNow);
    }

    private AppDbContext Db() => GetService<AppDbContext>();

    // ==================== حساب سیستمی ====================

    [Fact]
    public async Task Login_WithTheCorrectPassword_IsRefusedForASystemAccount_EvenIfItWereActive()
    {
        var system = await CreateUserAsync(isSystem: true, isActive: true);

        var response = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new { PhoneNumber = system.PhoneNumber, Password, DeviceId = "b44" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("accessToken");
    }

    [Fact]
    public async Task Refresh_IsRefused_OnceTheAccountIsASystemAccount()
    {
        var user = await CreateUserAsync(isSystem: false, isActive: true);
        var login = await Client.PostAsJsonAsync("/api/v1/auth/login",
            new { PhoneNumber = user.PhoneNumber, Password, DeviceId = "b44" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<TokenPair>();

        var db = Db();
        var tracked = await db.User.SingleAsync(u => u.UserId == user.UserId);
        tracked.IsSystem = true;
        await db.SaveChangesAsync();

        var refresh = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenDto { RefreshToken = tokens!.RefreshToken });

        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// OTP و بازیابی رمز هنوز وجود ندارند (Batch 46 و 47). این تست تضمین می‌کند هیچ‌کدام برای حساب سیستمی توکن یا پاسخ
    /// موفق نمی‌دهند؛ آن دو بسته باید از <see cref="User.CanSignIn"/> استفاده کنند و تست‌های خودشان را اضافه کنند.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/auth/otp/send")]
    [InlineData("/api/v1/auth/otp/verify")]
    [InlineData("/api/v1/auth/password/forgot")]
    [InlineData("/api/v1/auth/password/reset")]
    public async Task OtpAndPasswordReset_NeverIssueATokenForASystemAccount(string path)
    {
        var system = await CreateUserAsync(isSystem: true, isActive: false);

        var response = await Client.PostAsJsonAsync(path, new
        {
            PhoneNumber = system.PhoneNumber,
            Code = "123456",
            Purpose = "Login",
            NewPassword = "N3w-Passw0rd!"
        });

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("accessToken");
        body.Should().NotContain("refreshToken");
        response.IsSuccessStatusCode.Should().BeFalse();
        (await Db().User.AsNoTracking().SingleAsync(u => u.UserId == system.UserId)).PasswordHash
            .Should().Be(system.PasswordHash);
    }

    [Fact]
    public async Task AdminCannotActivate_AssignRoles_Edit_OrDelete_ASystemAccount()
    {
        var system = await CreateUserAsync(isSystem: true, isActive: false);
        var roleId = await Db().Role.AsNoTracking().Where(r => r.Code != "SuperAdmin").Select(r => r.RoleId).FirstAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminTokenAsync());

        var activate = await Client.PatchAsJsonAsync($"/api/v1/users/{system.UserId}", new UpdateUserStatusDto { UserId = system.UserId, IsActive = true });
        var roles = await Client.PutAsJsonAsync($"/api/v1/users/{system.UserId}/roles", new AssignRolesDto { RoleIds = new() { roleId } });
        var edit = await Client.PutAsJsonAsync($"/api/v1/users/{system.UserId}", new UpdateUserDto
        {
            UserId = system.UserId,
            PhoneNumber = system.PhoneNumber,
            FirstName = "Taken",
            LastName = "Over"
        });
        var delete = await Client.DeleteAsync($"/api/v1/users/{system.UserId}");

        foreach (var response in new[] { activate, roles, edit, delete })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict, response.RequestMessage!.Method + " " + response.RequestMessage.RequestUri);
            (await response.Content.ReadAsStringAsync()).Should().Contain("USER_IS_SYSTEM");
        }

        var after = await Db().User.AsNoTracking().Include(u => u.UserRoles).SingleAsync(u => u.UserId == system.UserId);
        after.IsActive.Should().BeFalse();
        after.FirstName.Should().Be("System");
        after.UserRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task AdminUserList_FlagsTheSystemAccount()
    {
        var system = await CreateUserAsync(isSystem: true, isActive: false);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminTokenAsync());

        var listed = await Client.GetFromJsonAsync<PagedUsers>(
            $"/api/v1/users?filter={Uri.EscapeDataString("issystem eq true")}&pageSize=50");

        listed!.Items.Should().ContainSingle(u => u.UserId == system.UserId).Which.IsSystem.Should().BeTrue();
        listed.Items.Should().OnlyContain(u => u.IsSystem);
    }

    [Fact]
    public async Task Bootstrapper_MarksTheAccountItCreates_AsSystem()
    {
        await RemovePlatformWarehousesAsync();

        var outcome = await CreateBootstrapper(ValidOptions("09124404401")).EnsureAsync(CancellationToken.None);

        outcome.Should().Be(PlatformWarehouseBootstrapOutcome.Created);
        var owner = await Db().User.AsNoTracking().SingleAsync(u => u.PhoneNumber == "09124404401");
        owner.IsSystem.Should().BeTrue();
        owner.IsActive.Should().BeFalse();
    }

    // ==================== حداکثر یک انبار پلتفرم ====================

    [Fact]
    public async Task ASecondPlatformWarehouse_IsRejectedByTheUniqueIndex()
    {
        var db = Db();
        var existing = await db.Warehouse.AsNoTracking().SingleAsync(w => w.SellerId == null);

        db.Warehouse.Add(new Warehouse { Name = "B44 second platform", AddressId = existing.AddressId, SellerId = null });
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        (await Db().Warehouse.AsNoTracking().CountAsync(w => w.SellerId == null)).Should().Be(1);
    }

    [Fact]
    public async Task AdminCreatingASecondPlatformWarehouse_Gets409PlatformWarehouseExists()
    {
        var existing = await Db().Warehouse.AsNoTracking().SingleAsync(w => w.SellerId == null);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAdminTokenAsync());

        var response = await Client.PostAsJsonAsync("/api/v1/warehouses",
            new CreateWarehouseDto { Name = "B44 platform 2", AddressId = existing.AddressId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PLATFORM_WAREHOUSE_EXISTS");
        (await Db().Warehouse.AsNoTracking().CountAsync(w => w.SellerId == null)).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentBootstrap_SecondCreatorFails_RollsBack_AndReloadsTheExistingWarehouse()
    {
        await RemovePlatformWarehousesAsync();
        var competitorAddressId = await CreateAddressAsync();
        var options = ValidOptions("09124404402");

        // «نمونهٔ دیگر» درست وقتی این نمونه تراکنش ساختش را آغاز می‌کند (پس از بررسی «انباری نیست») انبار پلتفرم را ثبت می‌کند.
        var competitor = new CompetitorInterceptor(competitorAddressId);
        var bootstrapper = CreateBootstrapper(options, new RacingScopeFactory(Factory.Services.GetRequiredService<IServiceScopeFactory>(), competitor));

        var outcome = await bootstrapper.EnsureAsync(CancellationToken.None);

        competitor.Fired.Should().BeTrue();
        outcome.Should().Be(PlatformWarehouseBootstrapOutcome.CreatedByAnotherInstance);

        var db = Db();
        var platform = await db.Warehouse.AsNoTracking().Where(w => w.SellerId == null).ToListAsync();
        platform.Should().ContainSingle().Which.Name.Should().Be(CompetitorInterceptor.WarehouseName);
        (await db.User.AsNoTracking().AnyAsync(u => u.PhoneNumber == "09124404402"))
            .Should().BeFalse("تراکنش نمونهٔ بازنده برمی‌گردد؛ حساب سیستمی نیمه‌کاره نمی‌ماند");
        (await db.Province.AsNoTracking().AnyAsync(p => p.ProvinceName == options.ProvinceName)).Should().BeFalse();

        // راه‌اندازی بعدی همان انبار را می‌بیند.
        (await CreateBootstrapper(options).EnsureAsync(CancellationToken.None))
            .Should().Be(PlatformWarehouseBootstrapOutcome.AlreadyExists);
    }

    // ==================== ابزار ====================

    private sealed class TokenPair
    {
        public string AccessToken { get; set; } = default!;
        public string RefreshToken { get; set; } = default!;
    }

    private sealed class PagedUsers
    {
        public List<UserDto> Items { get; set; } = new();
    }

    private async Task<User> CreateUserAsync(bool isSystem, bool isActive)
    {
        var db = Db();
        var user = new User
        {
            FirstName = "System",
            LastName = "B44",
            PhoneNumber = $"0912{Random.Shared.Next(1000000, 9999999)}",
            PasswordHash = GetService<IPasswordHasher>().Hash(Password),
            UserType = UserType.Employee,
            IsActive = isActive,
            IsSystem = isSystem,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(user);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return user;
    }

    private async Task<int> CreateAddressAsync()
    {
        var db = Db();
        var province = new Province { ProvinceName = "B44-Competitor-" + Guid.NewGuid().ToString("N")[..6] };
        db.Province.Add(province);
        await db.SaveChangesAsync();
        var city = new City { CityName = "B44-C", ProvinceId = province.ProvinceId };
        db.City.Add(city);
        await db.SaveChangesAsync();
        var address = new Address
        {
            CityId = city.CityId,
            StreetAddress = "Competitor St",
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "B44",
            RecipientLastName = "Competitor",
            UserId = 1
        };
        db.Address.Add(address);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return address.AddressId;
    }

    private async Task RemovePlatformWarehousesAsync()
    {
        var db = Db();
        db.Warehouse.RemoveRange(await db.Warehouse.Where(w => w.SellerId == null).ToListAsync());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private PlatformWarehouseBootstrapper CreateBootstrapper(PlatformWarehouseOptions options, IServiceScopeFactory? scopes = null) =>
        new(
            scopes ?? Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(options),
            new StubHostEnvironment("Testing"),
            NullLogger<PlatformWarehouseBootstrapper>.Instance);

    private static PlatformWarehouseOptions ValidOptions(string phone) => new()
    {
        Name = "انبار مرکزی B44-" + Guid.NewGuid().ToString("N")[..6],
        ProvinceName = "تهران B44-" + Guid.NewGuid().ToString("N")[..6],
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

    /// <summary>
    /// «نمونهٔ دیگر برنامه»: در لحظهٔ آغاز تراکنش ساختِ بوت‌استرپر (بیرون از آن تراکنش، با autocommit) انبار پلتفرم را درج می‌کند.
    /// </summary>
    private sealed class CompetitorInterceptor : DbTransactionInterceptor
    {
        public const string WarehouseName = "B44 competitor platform warehouse";
        private readonly int _addressId;

        public CompetitorInterceptor(int addressId) => _addressId = addressId;

        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO \"Warehouse\" (\"Name\", \"AddressId\", \"SellerId\", \"CreatedOn\", \"ModifiedOn\") " +
                    $"VALUES ('{WarehouseName}', {_addressId}, NULL, '2026-01-01 00:00:00', '2026-01-01 00:00:00')";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>scope هایی که AppDbContext آن‌ها رهگیر رقیب را دارد؛ بقیهٔ سرویس‌ها از scope واقعی می‌آیند.</summary>
    private sealed class RacingScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceScopeFactory _inner;
        private readonly CompetitorInterceptor _competitor;

        public RacingScopeFactory(IServiceScopeFactory inner, CompetitorInterceptor competitor)
        {
            _inner = inner;
            _competitor = competitor;
        }

        public IServiceScope CreateScope() => new RacingScope(_inner.CreateScope(), _competitor);

        private sealed class RacingScope : IServiceScope, IServiceProvider
        {
            private readonly IServiceScope _inner;
            private readonly CompetitorInterceptor _competitor;
            private AppDbContext? _db;

            public RacingScope(IServiceScope inner, CompetitorInterceptor competitor)
            {
                _inner = inner;
                _competitor = competitor;
            }

            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
            {
                if (serviceType != typeof(AppDbContext))
                    return _inner.ServiceProvider.GetService(serviceType);

                if (_db is null)
                {
                    var services = _inner.ServiceProvider;
                    var options = new DbContextOptionsBuilder<AppDbContext>(
                            services.GetRequiredService<DbContextOptions<AppDbContext>>())
                        .AddInterceptors(_competitor)
                        .Options;
                    _db = new TestAppDbContext(options, services.GetServices<ISaveChangesInterceptor>());
                }

                return _db;
            }

            public void Dispose()
            {
                _db?.Dispose();
                _inner.Dispose();
            }
        }
    }
}
