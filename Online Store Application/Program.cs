using Application.BackgroundJobs;
using Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Asp.Versioning;
using Application.Common.Specifications;
using Application.Entities;
using Application.Features.Users.CreateUser;
using Application.Interfaces;
using Application.Interfaces.Security;
using Application.Interfaces.Services;
using Application.Middleware;
using Application.Options;
using BusinessLogic.Extensions;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using DataLayer.Extensions;
using DataLayer.Security;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using Online_Store_Application.Authorization;
using Application.Exceptions;
using Online_Store_Application.Extensions;
using Online_Store_Application.Middleware;
using Online_Store_Application.Services;
using Online_Store_Application.Swagger;
using Serilog;
using Serilog.Enrichers.Span;
using System.Security.Claims;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .Enrich.WithSpan()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers(options =>
{
    options.ReturnHttpNotAcceptable = false;

    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
})
// Batch 40 (SEC-05): فقط JSON؛ formatterهای XML حذف شدند (بدنهٔ XML → 415، Accept: xml → همان JSON).
.AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ReferenceLoopHandling =
    Newtonsoft.Json.ReferenceLoopHandling.Ignore;
    // N14: سقف عمق بدنهٔ JSON صریح (همان پیش‌فرض Newtonsoft 13)؛ تست N14JsonDepthTests آن را قفل می‌کند.
    options.SerializerSettings.MaxDepth = Online_Store_Application.Middleware.JsonDepthGuardMiddleware.MaxDepth;
});

builder.Services.AddSingleton<Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory, CodedProblemDetailsFactory>();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = false;

    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(
                x => x.Key,
                x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Type = ErrorCodes.TypeUri(ErrorCodes.ValidationError.Code),
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Validation failed",
            Detail = ErrorCodes.ValidationError.Message,
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["code"] = ErrorCodes.ValidationError.Code;
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        return new UnprocessableEntityObjectResult(problem);
    };
});

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = false;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "توکن را وارد کنید"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();

var redisOptions = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>()
    ?? new RedisOptions
    {
        Configuration = "localhost:6379",
        InstanceName = "OnlineStore_Test:"
    };

builder.Services.AddRedis(redisOptions);

// Register OtelOptions with validation
builder.Services.AddOpenTelemetryOptions(builder.Configuration);

// Start OpenTelemetry SDK (traces + metrics, no logs yet)
builder.Services.AddOpenTelemetryServices(
    builder.Configuration,
    builder.Environment);

builder.Services.AddOptions<Application.Options.DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(Application.Options.DatabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var databaseOptions = builder.Configuration.GetSection("ConnectionStrings").Get<DatabaseOptions>()
    ?? new DatabaseOptions
    {
        DefaultConnection = "Data Source=:memory:"
    };

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection("ConnectionStrings"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDataLayerServices(databaseOptions);

builder.Services.AddApplicationHealthChecks(databaseOptions, redisOptions);

builder.Services.AddBusinessLogicServices();
builder.Services.AddAutoMapper(cfg => { }, typeof(CreateUserMappingProfile).Assembly);
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateUserCommandHandler>();
});
builder.Services.AddFluentValidationServices();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddHostedService<SuperAdminBootstrapper>();
// Batch 49b: انبار پلتفرم هنگام راه‌اندازی از PlatformWarehouse:* ساخته می‌شود؛ در Production بدون آن راه‌اندازی متوقف می‌شود.
builder.Services.AddPlatformWarehouseBootstrap(builder.Configuration);

// تصمیم ناظر: پرچم اجرای موجودی — پیش‌فرض سخت‌گیرانه (true).
builder.Services.Configure<InventorySettings>(
    builder.Configuration.GetSection(InventorySettings.SectionName));
builder.Services.Configure<CommissionSettings>(
    builder.Configuration.GetSection(CommissionSettings.SectionName));
// Batch 44 (FIN-02): تسویه تا HoldDays روز پس از تحویل (پنجرهٔ مرجوعی) قابل پرداخت نیست؛ مقدار نامعتبر راه‌اندازی را متوقف می‌کند.
builder.Services.AddOptions<PayoutSettings>()
    .Bind(builder.Configuration.GetSection(PayoutSettings.SectionName))
    .Validate(s => s.IsValid(), $"PayoutSettings:HoldDays must be between {PayoutSettings.MinHoldDays} and {PayoutSettings.MaxHoldDays}.")
    .ValidateOnStart();

builder.Services.Configure<StorageSettings>(
    builder.Configuration.GetSection(StorageSettings.SectionName));
builder.Services.AddProductImageUrlOptions(builder.Configuration);

// LocalStorageService از قرارداد نام‌گذاری XService/IXService پیروی نمی‌کند
// (IStorageService ≠ ILocalStorageService)، پس ثبت دستی لازم است؛
// وگرنه IProductImageService در زمان اجرا قابل ساخت نیست.
builder.Services.AddScoped<IStorageService, LocalStorageService>();
// ---------------- Phase 8: Payment ----------------
// F-01/F-14: در Production (و هر محیط غیر Development/Testing) راه‌اندازی شکست می‌خورد
// مگر اینکه Provider=Zarinpal، EnableRealGateway=true، MerchantId و WebhookSecret تنظیم شده باشند،
// و CorsOrigins با دامنهٔ واقعی فرانت‌اند مشخص شده باشد.
builder.Services.AddProductionSafeConfiguration(builder.Configuration);

builder.Services.AddHttpClient();

// انتخاب درگاه در زمان اجرا از روی تنظیمات؛ FakePaymentGateway فقط در Development/Testing
// ساخته می‌شود و در بقیهٔ محیط‌ها حل آن استثنا می‌دهد.
builder.Services.AddScoped<IPaymentGateway>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<PaymentGatewaySettings>>().Value;
    var environment = sp.GetRequiredService<IHostEnvironment>();

    return ProductionConfigurationExtensions.ResolveGatewayKind(settings.Provider, environment.EnvironmentName) switch
    {
        PaymentGatewayKind.Zarinpal => ActivatorUtilities.CreateInstance<ZarinpalPaymentGateway>(sp),
        _ => ActivatorUtilities.CreateInstance<FakePaymentGateway>(sp)
    };
});

// بدون ثبت صریح، اعتبارسنجی امضا وجود نداشت و همهٔ webhookها رد می‌شدند.
builder.Services.AddScoped<IPaymentSignatureValidator, PaymentSignatureValidator>();

// ---------------- Phase 9: Hardening ----------------
builder.Services.Configure<AbandonedPaymentSettings>(
    builder.Configuration.GetSection(AbandonedPaymentSettings.SectionName));

// TimeProvider تزریق می‌شود تا پاک‌سازی رهاشده‌ها در تست بدون انتظار واقعی قابل سنجش باشد.
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddHostedService<AbandonedPaymentCleanupService>();
builder.Services.AddHostedService<CartCleanupService>();
builder.Services.AddHostedService<ExpiredTokenCleanupService>();
builder.Services.AddHostedService<AbandonedOrderCleanupService>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddBackgroundProcessing(builder.Configuration);
    builder.Services.AddBackgroundJobHandler<MaintenanceHeartbeatJob, MaintenanceHeartbeatJobHandler>();
}
builder.Services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IQueryMetricsService, QueryMetricsService>();
builder.Services.AddScoped<ICacheService, RedisCacheService>();

// Batch 11 (F-13): JwtOptions و سیاست نشست (Session) با قید «بی‌کاری > عمر access token» هنگام راه‌اندازی سنجیده می‌شوند.
builder.Services.AddUserSessionOptions(builder.Configuration);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtOptions = builder.Configuration.GetSection(Application.Options.JwtOptions.SectionName)
        .Get<Application.Options.JwtOptions>() ?? new Application.Options.JwtOptions();

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = jwtOptions.Issuer,
        ValidAudience = jwtOptions.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtOptions.Key)),

        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.NameIdentifier
    };

    options.Events = new JwtBearerEvents
    {
        // Batch 53 (API-04): 401 و 403 خط لولهٔ JWT بدنهٔ ProblemDetails با کد پایدار دارند (TOKEN_EXPIRED، TOKEN_INVALID، SESSION_REVOKED، FORBIDDEN).
        OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
                ? "Bearer"
                : "Bearer error=\"invalid_token\"";
            await ProblemDetailsWriter.WriteAsync(context.HttpContext, ProblemDetailsWriter.ClassifyJwtFailure(context.AuthenticateFailure));
        },
        OnForbidden = context => ProblemDetailsWriter.WriteAsync(context.HttpContext, ErrorCodes.Forbidden),
        OnTokenValidated = async context =>
        {
            var unitOfWork = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();

            var userId = context.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var sessionId = context.Principal!.FindFirst("SessionId")?.Value;

            if (!int.TryParse(userId, out var uid) ||
                !Guid.TryParse(sessionId, out var sid))
            {
                context.Fail(new JwtFailure(ErrorCodes.TokenInvalid));
                return;
            }

            var isActive = await unitOfWork.Repository<UserSession>()
                .AnyAsync(x =>
                    x.UserId == uid &&
                    x.Id == sid &&
                    x.Status == UserSession.SessionStatus.Active);

            if (!isActive)
            {
                context.Fail(new JwtFailure(ErrorCodes.SessionRevoked));
                return;
            }

            var stamp = context.Principal!.FindFirst("SecurityStamp")?.Value;
            var dbUser = await unitOfWork.Repository<User>()
                .FirstOrDefaultAsync(new Spec<User>().Where(u => u.UserId == uid));

            if (dbUser == null || stamp == null || dbUser.SecurityStamp != stamp)
            {
                context.Fail(new JwtFailure(ErrorCodes.SessionRevoked));
                return;
            }
        }
    };
});

// Phase 1 (D8): نام Policyها ثابت مانده است؛ فقط بدنهٔ آن‌ها از RequireRole
// به مجوز مبتنی بر RolePermission تغییر کرده است.
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

builder.Services.AddAuthorization(options =>
{
    // Batch 40 (SEC-01): هر endpoint بدون ویژگی مجوز، به‌طور پیش‌فرض احراز هویت می‌خواهد.
    // endpointهای عمومی صریحاً [AllowAnonymous] / AllowAnonymous() دارند (فهرست در EndpointAuthorizationInventoryTests).
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .AddRequirements(new AuthenticatedByDefaultRequirement())
        .Build();

    options.AddPolicy("AdminOnly", policy =>
        policy.AddRequirements(new PermissionRequirement(Permissions.AdminFull)));

    options.AddPolicy("CanManageCatalog", policy =>
        policy.AddRequirements(new PermissionRequirement(Permissions.CatalogProductsWrite)));
});

var corsOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>()
    ?? new[]
    {
        "http://localhost:3000",
        "https://localhost:3000",
        "http://localhost:5173",
        "https://localhost:5173",
        "http://localhost:5174",
        "https://localhost:5174",
        "http://localhost:5178",
        "https://localhost:5178"
    };

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactFrontend",
        policy =>
        {
            policy.WithOrigins(corsOrigins)
                // AllowAnyHeader شامل هدر سفارشی سبد مهمان (X-Cart-Session) هم می‌شود،
                // پس preflight برای آن پاسخ می‌گیرد. اگر روزی این سیاست محدود شد،
                // این هدر باید صریحاً در WithHeaders باقی بماند.
                .AllowAnyHeader()
                .AllowAnyMethod();
            // Batch 40 (SEC-16): AllowCredentials حذف شد؛ API کوکی ندارد و توکن در هدر Authorization می‌آید.
        });
});

// F-20: فقط X-Forwarded-For/Proto پروکسی‌های صریحاً تنظیم‌شده پذیرفته می‌شود (ForwardedHeaders:*).
builder.Services.AddTrustedForwardedHeaders(builder.Configuration);

// F-03: محدودیت نرخ به‌ازای هر کلاینت (IP یا شناسهٔ کاربر)؛ سقف‌ها از RateLimiting:* خوانده می‌شوند.
builder.Services.AddPartitionedRateLimiting(builder.Configuration);

// F-04: کش خروجی opt-in است؛ سیاست پایه ندارد و فقط endpointهای دارای سیاست نام‌دار کش می‌شوند
// (OutputCachePolicies). سبد، موجودی، احراز هویت و health صراحتاً NoStore هستند.
var outputCacheEnabled = OutputCacheExtensions.IsOutputCacheEnabled(builder.Configuration, builder.Environment);

if (outputCacheEnabled)
{
    builder.Services.AddApplicationOutputCache(builder.Environment, redisOptions);
}

var app = builder.Build();

// Batch 40 (SEC-06): AllowedHosts از پیکربندی خوانده می‌شود (HostFiltering)؛ «*» در Production فقط هشدار است.
foreach (var warning in ProductionConfigurationRules.AllowedHostsWarnings(
             app.Configuration["AllowedHosts"], app.Environment.EnvironmentName))
{
    app.Logger.LogWarning("{AllowedHostsWarning}", warning);
}

// Staging: مایگریشن قبل از سرو کردن اولین درخواست.
// اگر شکست بخورد، استثنا بالا می‌رود و کانتینر healthy نمی‌شود —
// بهتر از سرو کردن با اسکیمای قدیمی.
await app.Services.ApplyMigrationsAsync(
    app.Configuration,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AutoMigrate"));

// F-20: اولین middleware، تا محدودیت نرخ، احراز هویت، لاگ‌ها و نشست‌ها IP واقعی کلاینت را ببینند.
app.UseTrustedForwardedHeaders();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<JsonDepthGuardMiddleware>();

// Batch 53 (API-04): پاسخ‌های بدون بدنه (مسیر ناموجود، متد یا نوع محتوای ناپشتیبانی‌شده) بدنهٔ ProblemDetails با code می‌گیرند.
app.UseStatusCodePages(async statusContext =>
{
    var http = statusContext.HttpContext;
    var info = http.Response.StatusCode switch
    {
        StatusCodes.Status404NotFound => http.GetEndpoint() is null ? ErrorCodes.RouteNotFound : ErrorCodes.NotFound,
        StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
        StatusCodes.Status415UnsupportedMediaType => ErrorCodes.UnsupportedMediaType,
        _ => null
    };

    if (info is not null)
        await ProblemDetailsWriter.WriteAsync(http, info);
});

// pipeline
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        var provider = app.Services.GetRequiredService<Asp.Versioning.ApiExplorer.IApiVersionDescriptionProvider>();

        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerEndpoint(
                $"/swagger/{description.GroupName}/swagger.json",
                description.GroupName.ToUpperInvariant());
        }
    });
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    // Batch 40 (SEC-06): پاسخ‌های API و فایل‌های آپلودشده هرگز به‌عنوان صفحه اجرا نمی‌شوند.
    // Swagger UI پیش از این middleware پاسخ می‌دهد و این هدر را نمی‌گیرد.
    context.Response.Headers.TryAdd("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
    await next();
});

app.UseStaticFiles();

app.UseRouting();

// Batch 52 (OPS-07): خروجی تفصیلی بیرون از Development فقط برای مدیر؛ live/ready عمومی می‌مانند.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => !check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var result = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration,
                description = e.Value.Description,
                tags = e.Value.Tags
            })
        };

        await context.Response.WriteAsJsonAsync(result);
    }
}).RequireDetailedHealthAccess(app.Environment).CacheOutput(policy => policy.NoCache());

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous().CacheOutput(policy => policy.NoCache());

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous().CacheOutput(policy => policy.NoCache());

app.UseCors("ReactFrontend");

app.UseMiddleware<QueryMetricsMiddleware>();

app.UseAuthentication();

// F-03: پس از احراز هویت، تا سیاست‌های checkout و پرداخت بر اساس شناسهٔ کاربر کلید بخورند.
app.UseRateLimiter();

app.UseAuthorization();

// F-04: پس از CORS و احراز هویت، تا پاسخ کش‌شده از کنار این دو عبور نکند.
if (outputCacheEnabled)
{
    app.UseOutputCache();
}

app.MapControllers();

// T2 code-trigger verification
app.MapGet("/version", () => new
{
    version = "v2",
    commit = Environment.GetEnvironmentVariable("GIT_COMMIT") ?? "unknown",
    deployedAt = DateTime.UtcNow
}).AllowAnonymous().CacheOutput(policy => policy.NoCache());


Log.Information("Online Store API started successfully");
Console.WriteLine("Application is running...");

app.Run();


public partial class Program { }