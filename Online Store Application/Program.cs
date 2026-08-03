using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using Application.Interfaces.Security;
using Application.Middleware;
using BusinessLogic.Extensions;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using DataLayer.Extensions;
using DataLayer.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Online_Store_Application.Middleware;
using Online_Store_Application.Services;
using Serilog;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;


var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers(options =>
{
    options.ReturnHttpNotAcceptable = true;

    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
})
.AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ReferenceLoopHandling =
        Newtonsoft.Json.ReferenceLoopHandling.Ignore;
})
.AddXmlDataContractSerializerFormatters();

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
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Validation failed",
            Detail = "One or more validation errors occurred.", 
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["code"] = "VALIDATION_ERROR";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        return new UnprocessableEntityObjectResult(problem);
    };
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



builder.Services.AddDataLayerServices(builder.Configuration);
builder.Services.AddBusinessLogicServices();
builder.Services.AddFluentValidationServices();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IQueryMetricsService, QueryMetricsService>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),

        RoleClaimType = ClaimTypes.Role,
        NameClaimType = ClaimTypes.NameIdentifier
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var unitOfWork = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();

            var userId = context.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var sessionId = context.Principal!.FindFirst("SessionId")?.Value;

            if (!int.TryParse(userId, out var uid) ||
                !Guid.TryParse(sessionId, out var sid))
            {
                context.Fail("Invalid token claims");
                return;
            }

            var isActive = await unitOfWork.Repository<UserSession>()
                .AnyAsync(x =>
                    x.UserId == uid &&
                    x.Id == sid &&
                    x.Status == UserSession.SessionStatus.Active);

            if (!isActive)
            {
                context.Fail("Session revoked");
                return;
            }

            // SecurityStamp check: ensure user's current stamp matches token claim
            var stamp = context.Principal!.FindFirst("SecurityStamp")?.Value;

            var dbUser = await unitOfWork.Repository<User>()
                .FirstOrDefaultAsync(new Spec<User>().Where(u => u.UserId == uid));

            if (dbUser == null || stamp == null || dbUser.SecurityStamp != stamp)
            {
                context.Fail("Security stamp invalid");
                return;
            }
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanManageCatalog", policy =>
        policy.RequireRole("Admin", "Manager"));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactFrontend",
        policy =>
        {
            policy.WithOrigins(
                    "https://localhost:5173",
                    "http://localhost:5173",
                    "http://localhost:5178"
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

builder.Services.AddRateLimiter(options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.AddFixedWindowLimiter(
            "LoginLimiter",
            config =>
            {
                config.PermitLimit = 10000;
                config.Window = TimeSpan.FromMinutes(1);
                config.QueueLimit = 0;
            });

        options.AddFixedWindowLimiter(
            "RefreshLimiter",
            config =>
            {
                config.PermitLimit = 10000;
                config.Window = TimeSpan.FromMinutes(1);
                config.QueueLimit = 0;
            });
    }
    else
    {
        options.AddFixedWindowLimiter(
            "LoginLimiter",
            config =>
            {
                config.PermitLimit = 5;
                config.Window = TimeSpan.FromMinutes(1);
                config.QueueLimit = 0;
                config.QueueProcessingOrder =
                    QueueProcessingOrder.OldestFirst;
            });

        options.AddFixedWindowLimiter(
            "RefreshLimiter",
            config =>
            {
                config.PermitLimit = 20;
                config.Window = TimeSpan.FromMinutes(1);
                config.QueueLimit = 0;
                config.QueueProcessingOrder =
                    QueueProcessingOrder.OldestFirst;
            });
    }
});

var app = builder.Build();


app.UseMiddleware<ExceptionHandlingMiddleware>();


using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (!app.Environment.IsEnvironment("Testing") && context.Database.IsRelational())
    {
        var retryCount = 0;
        const int maxRetries = 10;
        while (retryCount < maxRetries)
        {
            try
            {
                await context.Database.MigrateAsync();
                break;
            }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException ||
                                       ex is System.TimeoutException ||
                                       ex.InnerException is System.TimeoutException)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                    throw;
                Console.WriteLine($"Database not ready, retrying in 5 seconds... (attempt {retryCount})");
                await Task.Delay(5000);
            }
        }
    }
}

// pipeline
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();

app.UseRouting();

app.UseCors("ReactFrontend");

app.UseRateLimiter();

app.UseMiddleware<QueryMetricsMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();


Log.Information("Online Store API started successfully");
Console.WriteLine("Application is running...");

app.Run();


public partial class Program { }