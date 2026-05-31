using BusinessLogic.Extensions;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using BusinessLogic.Services.Implementations;
using Online_Store_Application.Services;
using Application.Interfaces;
using Application.Middleware;
using DataLayer.Extensions;
using DataLayer.Security;
using Application.Interfaces.Security;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;


var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers(options =>
{
    options.ReturnHttpNotAcceptable = true;
})
.AddNewtonsoftJson(options =>
{
    options.SerializerSettings.ReferenceLoopHandling =
        Newtonsoft.Json.ReferenceLoopHandling.Ignore;
})
.AddXmlDataContractSerializerFormatters();

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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!app.Environment.IsEnvironment("Testing") &&
        context.Database.IsRelational())
    {
        context.Database.Migrate();
    }
}

// pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseCors("ReactFrontend");
app.UseMiddleware<QueryMetricsMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.UseStaticFiles();


Log.Information("Online Store API started successfully");
Console.WriteLine("Application is running...");

app.Run();


public partial class Program { }