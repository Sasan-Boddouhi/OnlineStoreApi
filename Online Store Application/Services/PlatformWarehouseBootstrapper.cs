using System.Security.Cryptography;
using Application.Entities;
using Application.Interfaces.Security;
using Application.Options;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Online_Store_Application.Services;

/// <summary>نتیجهٔ یک اجرای <see cref="PlatformWarehouseBootstrapper.EnsureAsync"/>.</summary>
public enum PlatformWarehouseBootstrapOutcome
{
    /// <summary>دست‌کم یک انبار پلتفرم (SellerId == null) از قبل بود؛ هیچ تغییری داده نشد.</summary>
    AlreadyExists,

    /// <summary>انبار پلتفرم از روی <see cref="PlatformWarehouseOptions"/> ساخته شد.</summary>
    Created,

    /// <summary>بیرون از Production: انباری نبود و پیکربندی ناقص بود؛ فقط هشدار ثبت شد.</summary>
    NotConfigured
}

/// <summary>
/// Batch 49b — تضمین وجود انبار پلتفرم هنگام راه‌اندازی (پاسخ مالک به Batch 49).
/// <para>
/// idempotent: اگر هر انبار پلتفرمی باشد کاری نمی‌کند. وگرنه از <see cref="PlatformWarehouseOptions"/>
/// استان و شهر (با همان نام، یا تازه)، نشانی و انبار را در یک تراکنش می‌سازد.
/// مالک نشانی (Address.UserId الزامی است) کاربری با همان شمارهٔ نرمال‌شدهٔ <c>PlatformWarehouse:PhoneNumber</c> است؛
/// اگر چنین کاربری نباشد یک حساب سیستمی <b>غیرفعال</b> با رمز تصادفیِ نامعلوم ساخته می‌شود (ورود با آن ممکن نیست)،
/// چون در نخستین استقرار هنوز هیچ مدیری ثبت‌نام نکرده است.
/// </para>
/// <para>
/// در Production اگر انباری نباشد و پیکربندی ناقص یا نامعتبر باشد، <see cref="OptionsValidationException"/>
/// راه‌اندازی را متوقف می‌کند. بیرون از Production فقط هشدار ثبت می‌شود و 409
/// <c>PLATFORM_WAREHOUSE_NOT_CONFIGURED</c> همچنان تور ایمنی است.
/// خطای دسترسی به پایگاه داده (مثلاً اسکیمای اجرانشده) مانند <see cref="SuperAdminBootstrapper"/> فقط ثبت می‌شود:
/// وضعیت پایگاه داده را health check گزارش می‌کند و این سرویس نمی‌تواند دربارهٔ انبار تصمیم بگیرد.
/// </para>
/// <para>
/// IHostedService (نه BackgroundService) است تا پیش از پذیرش نخستین درخواست کامل شود و استثنایش راه‌اندازی را متوقف کند.
/// </para>
/// </summary>
public sealed class PlatformWarehouseBootstrapper : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PlatformWarehouseOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PlatformWarehouseBootstrapper> _logger;

    public PlatformWarehouseBootstrapper(
        IServiceScopeFactory scopeFactory,
        IOptions<PlatformWarehouseOptions> options,
        IHostEnvironment environment,
        ILogger<PlatformWarehouseBootstrapper> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureAsync(cancellationToken);
        }
        catch (OptionsValidationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // خاموش شدن برنامه هنگام راه‌اندازی — طبیعی است.
        }
        catch (Exception ex)
        {
            _logger.Log(
                IsProduction ? LogLevel.Error : LogLevel.Warning,
                ex,
                "Platform warehouse check skipped: the database could not be queried. " +
                "Stock without a warehouse returns 409 PLATFORM_WAREHOUSE_NOT_CONFIGURED until a platform warehouse exists.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// اگر انبار پلتفرمی نباشد، آن را از پیکربندی می‌سازد. در Production با پیکربندی ناقص
    /// <see cref="OptionsValidationException"/> می‌دهد.
    /// </summary>
    public async Task<PlatformWarehouseBootstrapOutcome> EnsureAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.Warehouse.AsNoTracking().AnyAsync(w => w.SellerId == null, cancellationToken))
            return PlatformWarehouseBootstrapOutcome.AlreadyExists;

        var options = _options.Value;
        var errors = options.Validate();

        if (errors.Count > 0)
        {
            if (IsProduction)
            {
                throw new OptionsValidationException(
                    Microsoft.Extensions.Options.Options.DefaultName,
                    typeof(PlatformWarehouseOptions),
                    new[]
                    {
                        "هیچ انبار پلتفرمی (Warehouse با SellerId = null) وجود ندارد و بخش PlatformWarehouse برای ساختن آن " +
                        "کامل نیست؛ متغیرهای PlatformWarehouse__* را تنظیم کنید (docs/deployment.md §8)."
                    }.Concat(errors));
            }

            _logger.LogWarning(
                "No platform warehouse exists and the PlatformWarehouse section is incomplete ({Errors}). " +
                "Stock without a warehouse returns 409 PLATFORM_WAREHOUSE_NOT_CONFIGURED.",
                string.Join(" | ", errors));
            return PlatformWarehouseBootstrapOutcome.NotConfigured;
        }

        await CreateAsync(db, scope.ServiceProvider, options, cancellationToken);
        return PlatformWarehouseBootstrapOutcome.Created;
    }

    private async Task CreateAsync(
        AppDbContext db,
        IServiceProvider services,
        PlatformWarehouseOptions options,
        CancellationToken cancellationToken)
    {
        var phone = options.NormalizedPhoneNumber!;
        var provinceName = options.ProvinceName.Trim();
        var cityName = options.CityName.Trim();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var province = await db.Province.FirstOrDefaultAsync(p => p.ProvinceName == provinceName, cancellationToken);
        if (province is null)
        {
            province = new Province { ProvinceName = provinceName };
            db.Province.Add(province);
            await db.SaveChangesAsync(cancellationToken);
        }

        var city = await db.City.FirstOrDefaultAsync(
            c => c.ProvinceId == province.ProvinceId && c.CityName == cityName, cancellationToken);
        if (city is null)
        {
            city = new City { CityName = cityName, ProvinceId = province.ProvinceId };
            db.City.Add(city);
            await db.SaveChangesAsync(cancellationToken);
        }

        var owner = await db.User.FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);
        var createdSystemAccount = false;
        if (owner is null)
        {
            // رمز تصادفی که هیچ‌جا ذخیره یا نمایش داده نمی‌شود؛ IsActive=false ورود را هم می‌بندد.
            var unusablePassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            owner = new User
            {
                FirstName = options.RecipientFirstName.Trim(),
                LastName = options.RecipientLastName.Trim(),
                PhoneNumber = phone,
                PasswordHash = services.GetRequiredService<IPasswordHasher>().Hash(unusablePassword),
                UserType = UserType.Employee,
                IsActive = false
            };
            db.User.Add(owner);
            await db.SaveChangesAsync(cancellationToken);
            createdSystemAccount = true;
        }

        var address = new Address
        {
            CityId = city.CityId,
            StreetAddress = options.StreetAddress.Trim(),
            Plaque = options.Plaque.Trim(),
            Unit = string.IsNullOrWhiteSpace(options.Unit) ? PlatformWarehouseOptions.DefaultUnit : options.Unit.Trim(),
            PostalCode = options.PostalCode.Trim(),
            RecipientFirstName = options.RecipientFirstName.Trim(),
            RecipientLastName = options.RecipientLastName.Trim(),
            ExtraDescription = $"تلفن مسئول انبار: {phone}",
            UserId = owner.UserId
        };
        db.Address.Add(address);
        await db.SaveChangesAsync(cancellationToken);

        var warehouse = new Warehouse
        {
            Name = options.Name.Trim(),
            AddressId = address.AddressId,
            SellerId = null
        };
        db.Warehouse.Add(warehouse);
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Platform warehouse {WarehouseId} created from configuration (address owner {UserId}, new system account: {Created}).",
            warehouse.WarehouseId, owner.UserId, createdSystemAccount);
    }

    private bool IsProduction => ProductionConfigurationRules.IsProduction(_environment.EnvironmentName);
}
