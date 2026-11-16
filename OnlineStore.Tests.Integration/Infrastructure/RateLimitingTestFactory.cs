using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// F-03/F-20: میزبان تست با سقف‌های واقعی و کوچک محدودیت نرخ.
/// کارخانهٔ پیش‌فرض همهٔ سقف‌ها را ۱۰۰۰۰ می‌گذارد، و به همین دلیل سراسری بودن limiterها هیچ‌وقت دیده نشد.
/// محیط Development است (نه Testing) تا همان مسیر غیرتست pipeline اجرا شود.
/// آدرس طرف اتصال (peer) با هدر آزمایشی <see cref="PeerIpHeader"/> شبیه‌سازی می‌شود، چون TestServer آن را خالی می‌گذارد.
/// </summary>
public sealed class RateLimitingTestFactory : IntegrationTestFactory<Program>
{
    /// <summary>هدری که فقط در این میزبان تست، آدرس TCP طرف اتصال را تعیین می‌کند.</summary>
    public const string PeerIpHeader = "X-Test-Peer-Ip";

    /// <summary>تنها پروکسی مورد اعتماد در این میزبان.</summary>
    public const string TrustedProxyIp = "10.10.0.5";

    public const int LoginLimit = 5;
    public const int RegisterLimit = 5;
    public const int CheckoutLimit = 5;
    public const int PaymentVerifyLimit = 10;
    public const int OtpSendLimit = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Login:PermitLimit"] = LoginLimit.ToString(),
            ["RateLimiting:Login:WindowSeconds"] = "60",
            ["RateLimiting:Register:PermitLimit"] = RegisterLimit.ToString(),
            ["RateLimiting:Register:WindowSeconds"] = "60",
            ["RateLimiting:Refresh:PermitLimit"] = "20",
            ["RateLimiting:Refresh:WindowSeconds"] = "60",
            ["RateLimiting:Checkout:PermitLimit"] = CheckoutLimit.ToString(),
            ["RateLimiting:Checkout:WindowSeconds"] = "60",
            ["RateLimiting:PaymentInitiate:PermitLimit"] = "10",
            ["RateLimiting:PaymentInitiate:WindowSeconds"] = "60",
            ["RateLimiting:PaymentVerify:PermitLimit"] = PaymentVerifyLimit.ToString(),
            ["RateLimiting:PaymentVerify:WindowSeconds"] = "60",
            ["RateLimiting:OtpSend:PermitLimit"] = OtpSendLimit.ToString(),
            ["RateLimiting:OtpSend:WindowSeconds"] = "3600",
            ["ForwardedHeaders:KnownProxies:0"] = TrustedProxyIp,
            ["ForwardedHeaders:ForwardLimit"] = "1",
            ["OpenTelemetry:Enabled"] = "false",
            ["OutputCaching:Enabled"] = "false"
        }));

        builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, PeerIpStartupFilter>());
    }

    /// <summary>پیش از کل pipeline برنامه اجرا می‌شود؛ همان جایی که Kestrel آدرس واقعی اتصال را می‌گذارد.</summary>
    private sealed class PeerIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(PeerIpHeader, out var value)
                    && IPAddress.TryParse(value.ToString(), out var peer))
                {
                    context.Connection.RemoteIpAddress = peer;
                }

                await nextMiddleware();
            });

            next(app);
        };
    }
}
