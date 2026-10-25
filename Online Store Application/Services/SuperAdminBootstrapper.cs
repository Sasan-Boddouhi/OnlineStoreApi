using Application.Entities;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;

namespace Online_Store_Application.Services;

/// <summary>
/// بوت‌استرپ یک‌بارهٔ SuperAdmin (Phase 1).
///
/// قواعد امنیتی:
/// - هیچ هاردکد اعتبارنامه‌ای وجود ندارد (به‌تبعیت از SEC-02).
/// - منبع هویت فقط env var «BOOTSTRAP_SUPERADMIN_PHONE» است.
/// - اگر کاربر پیدا نشد یا متغیر تنظیم نشده بود: فقط LogWarning و عدم crash.
/// - idempotent: اگر هر کاربری از قبل SuperAdmin باشد، هیچ کاری نمی‌کند.
/// - BackgroundService است تا راه‌اندازی برنامه به در دسترس بودن دیتابیس گره نخورد.
/// </summary>
public sealed class SuperAdminBootstrapper : BackgroundService
{
    private const string SuperAdminRoleCode = "SuperAdmin";
    private const string BootstrapPhoneKey = "BOOTSTRAP_SUPERADMIN_PHONE";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SuperAdminBootstrapper> _logger;

    public SuperAdminBootstrapper(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SuperAdminBootstrapper> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var superAdminRole = await db.Role
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Code == SuperAdminRoleCode, stoppingToken);

            if (superAdminRole is null)
            {
                _logger.LogWarning(
                    "SuperAdmin bootstrap skipped: role '{RoleCode}' not found. Apply the RBAC migration first.",
                    SuperAdminRoleCode);
                return;
            }

            var alreadyBootstrapped = await db.UserRole
                .AsNoTracking()
                .AnyAsync(ur => ur.RoleId == superAdminRole.RoleId, stoppingToken);

            if (alreadyBootstrapped)
                return;

            var phone = _configuration[BootstrapPhoneKey];

            if (string.IsNullOrWhiteSpace(phone))
            {
                _logger.LogWarning(
                    "SuperAdmin bootstrap skipped: '{Key}' is not set. No SuperAdmin exists yet.",
                    BootstrapPhoneKey);
                return;
            }

            var user = await db.User
                .FirstOrDefaultAsync(u => u.PhoneNumber == phone, stoppingToken);

            if (user is null)
            {
                _logger.LogWarning(
                    "SuperAdmin bootstrap skipped: no user found for the configured phone number.");
                return;
            }

            db.UserRole.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = superAdminRole.RoleId
            });

            await db.SaveChangesAsync(stoppingToken);

            _logger.LogInformation(
                "SuperAdmin bootstrap completed for user {UserId}.", user.UserId);
        }
        catch (OperationCanceledException)
        {
            // خاموش شدن برنامه — طبیعی است.
        }
        catch (Exception ex)
        {
            // استارپ نباید هرگز به این مرحله وابسته باشد.
            _logger.LogWarning(ex, "SuperAdmin bootstrap skipped due to an unexpected error.");
        }
    }
}