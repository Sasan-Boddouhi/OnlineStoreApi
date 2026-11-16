using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using BusinessLogic.DTOs.Auth;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// F-03: هر limiter باید برای هر کلاینت سهمیهٔ جداگانه داشته باشد (IP برای endpointهای ناشناس،
/// شناسهٔ کاربر برای endpointهای احرازشده)، و پاسخ 429 باید Retry-After و بدنهٔ استاندارد خطا داشته باشد.
/// F-20: IP کلاینت فقط از پروکسی‌های مورد اعتماد پذیرفته می‌شود و همان در نشست ذخیره می‌شود.
/// هر تست آدرس‌های مخصوص خودش را دارد، چون وضعیت limiter در طول عمر factory باقی می‌ماند.
/// ورود با شمارهٔ ناموجود عمداً استفاده می‌شود: پاسخ عادی آن 401 است و قفل حساب را فعال نمی‌کند،
/// پس هر پاسخ دیگری (429، یا 503 پیش‌فرض قدیمی) یعنی limiter درخواست را رد کرده است.
/// </summary>
public class RateLimitingPartitionTests : IClassFixture<RateLimitingTestFactory>, IAsyncLifetime
{
    private const string LoginUrl = "/api/v1/auth/login";
    private const string RegisterUrl = "/api/v1/auth/register";
    private const string CheckoutUrl = "/api/v1/carts/checkout";
    private const string OtpSendUrl = "/api/v1/auth/otp/send";

    private readonly RateLimitingTestFactory _factory;
    private readonly HttpClient _client;

    public RateLimitingPartitionTests(RateLimitingTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Login_TwoClientIps_EachGetTheirOwnQuota()
    {
        const string clientA = "203.0.113.11";
        const string clientB = "203.0.113.12";

        for (var i = 0; i < RateLimitingTestFactory.LoginLimit; i++)
            (await PostAsync(LoginUrl, UnknownLogin(i), clientA)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, $"درخواست {i + 1} هنوز در سهمیهٔ A است");

        var fromB = await PostAsync(LoginUrl, UnknownLogin(100), clientB);
        fromB.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "مصرف سهمیهٔ کلاینت A نباید کلاینت B را مسدود کند");

        var aOverLimit = await PostAsync(LoginUrl, UnknownLogin(101), clientA);
        aOverLimit.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_OneIpOverLimit_Returns429WithRetryAfterAndStandardErrorBody()
    {
        const string client = "203.0.113.21";

        for (var i = 0; i < RateLimitingTestFactory.LoginLimit; i++)
            (await PostAsync(LoginUrl, UnknownLogin(i), client)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized);

        var rejected = await PostAsync(LoginUrl, UnknownLogin(99), client);

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        rejected.Headers.RetryAfter!.Delta.Should().NotBeNull();
        rejected.Headers.RetryAfter.Delta!.Value.TotalSeconds.Should().BeInRange(1, 60);
        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().Should().Be(429);
        body.RootElement.GetProperty("code").GetString().Should().Be("RATE_LIMIT_EXCEEDED");
        body.RootElement.GetProperty("detail").GetString().Should().Contain("تلاش کنید");
        body.RootElement.GetProperty("retryAfterSeconds").GetInt32()
            .Should().Be((int)rejected.Headers.RetryAfter.Delta.Value.TotalSeconds);
    }

    [Fact]
    public async Task Checkout_TwoAuthenticatedUsers_HaveIndependentQuotas_EvenBehindTheSameIp()
    {
        var tokenA = await RegisterAsync("09130000001", "203.0.113.31");
        var tokenB = await RegisterAsync("09130000002", "203.0.113.32");

        // هر دو کاربر پشت یک NAT؛ اگر کلید IP بود سهمیه مشترک می‌شد.
        const string sharedIp = "203.0.113.33";

        for (var i = 0; i < RateLimitingTestFactory.CheckoutLimit; i++)
            ShouldNotBeRateLimited(await PostAsync(CheckoutUrl, new { }, sharedIp, bearer: tokenA),
                $"checkout {i + 1} هنوز در سهمیهٔ کاربر A است");

        ShouldNotBeRateLimited(await PostAsync(CheckoutUrl, new { }, sharedIp, bearer: tokenB),
            "مصرف سهمیهٔ کاربر A نباید checkout کاربر B را مسدود کند");

        var aOverLimit = await PostAsync(CheckoutUrl, new { }, sharedIp, bearer: tokenA);
        aOverLimit.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        aOverLimit.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task SpoofedForwardedFor_FromUntrustedPeer_IsIgnored_SameBucketStill429()
    {
        const string untrustedPeer = "203.0.113.41";

        for (var i = 0; i < RateLimitingTestFactory.LoginLimit; i++)
            (await PostAsync(LoginUrl, UnknownLogin(i), untrustedPeer, forwardedFor: $"198.51.100.{i + 1}"))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var spoofed = await PostAsync(LoginUrl, UnknownLogin(99), untrustedPeer, forwardedFor: "198.51.100.99");

        spoofed.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "X-Forwarded-For از یک peer نامطمئن نباید سهمیهٔ تازه بدهد");
    }

    [Fact]
    public async Task ForwardedFor_FromTrustedProxy_IsHonored()
    {
        const string clientA = "198.51.100.150";
        const string clientB = "198.51.100.151";

        for (var i = 0; i < RateLimitingTestFactory.LoginLimit; i++)
            (await PostAsync(LoginUrl, UnknownLogin(i), RateLimitingTestFactory.TrustedProxyIp, forwardedFor: clientA))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await PostAsync(LoginUrl, UnknownLogin(98), RateLimitingTestFactory.TrustedProxyIp, forwardedFor: clientA))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "کلاینت A از پشت پروکسی سهمیه‌اش را تمام کرده است");

        var fromB = await PostAsync(LoginUrl, UnknownLogin(99), RateLimitingTestFactory.TrustedProxyIp, forwardedFor: clientB);
        fromB.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "پشت پروکسی مورد اعتماد، کلید سهمیه IP واقعی کلاینت است نه IP پروکسی");
    }

    [Fact]
    public async Task Login_ThroughTrustedProxy_StoresResolvedClientIpAndHeaderUserAgent_NotBodyValues()
    {
        const string realClientIp = "198.51.100.77";

        var response = await PostAsync(LoginUrl, new
        {
            PhoneNumber = "09123456789",
            Password = "Test@123",
            DeviceId = "rate-limit-session-test",
            IpAddress = "6.6.6.6",
            UserAgent = "spoofed-body-agent"
        }, RateLimitingTestFactory.TrustedProxyIp, forwardedFor: realClientIp, userAgent: "RateLimitTests/1.0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.Set<UserSession>().AsNoTracking()
            .Where(s => s.DeviceId == "rate-limit-session-test")
            .SingleAsync();

        session.IpAddress.Should().Be(realClientIp);
        session.UserAgent.Should().Be("RateLimitTests/1.0");
    }

    [Fact]
    public async Task Batch46_OtpSend_IsLimitedPerClientIp_AcrossDifferentPhones()
    {
        const string clientA = "203.0.113.51";
        const string clientB = "203.0.113.52";

        // a different phone each time: the per-phone limit never applies, only the per-IP one.
        for (var i = 0; i < RateLimitingTestFactory.OtpSendLimit; i++)
            (await PostAsync(OtpSendUrl, new { phoneNumber = $"0913510{i:D4}" }, clientA)).StatusCode
                .Should().Be(HttpStatusCode.OK, $"send {i + 1} is still inside the quota of A");

        var aOverLimit = await PostAsync(OtpSendUrl, new { phoneNumber = "09135109999" }, clientA);
        aOverLimit.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        aOverLimit.Headers.RetryAfter.Should().NotBeNull();
        _factory.Sms.MessagesTo("09135109999").Should().BeEmpty("a rejected request sends no SMS");

        (await PostAsync(OtpSendUrl, new { phoneNumber = "09135108888" }, clientB)).StatusCode
            .Should().Be(HttpStatusCode.OK, "client A's quota must not block client B");
    }

    // ---------------- helpers ----------------

    private static void ShouldNotBeRateLimited(HttpResponseMessage response, string because) =>
        new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable }
            .Should().NotContain(response.StatusCode, because);

    private static object UnknownLogin(int n) => new
    {
        PhoneNumber = $"0912000{n:D4}",
        Password = "Wrong@123",
        DeviceId = "rate-limit-test"
    };

    private async Task<string> RegisterAsync(string phone, string peerIp)
    {
        // Batch 46: registration needs a verified phone (two-step flow); the helper adds the verification token.
        var response = await _client.PostRegisterWithVerifiedPhoneAsync(_factory, new
        {
            PhoneNumber = phone,
            Password = "Test@123",
            FirstName = "کاربر",
            LastName = "آزمون",
            DeviceId = "rate-limit-test"
        }, request => request.Headers.Add(RateLimitingTestFactory.PeerIpHeader, peerIp));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        return auth!.AccessToken;
    }

    private Task<HttpResponseMessage> PostAsync(
        string url,
        object body,
        string peerIp,
        string? forwardedFor = null,
        string? bearer = null,
        string? userAgent = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add(RateLimitingTestFactory.PeerIpHeader, peerIp);

        if (forwardedFor != null)
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        if (bearer != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (userAgent != null)
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

        return _client.SendAsync(request);
    }
}

/// <summary>F-03/F-20: تنظیمات نامعتبر محدودیت نرخ یا پروکسی، راه‌اندازی را متوقف می‌کند (ValidateOnStart).</summary>
public class RateLimitingConfigurationValidationTests
{
    private sealed class SettingsFactory : IntegrationTestFactory<Program>
    {
        private readonly Dictionary<string, string?> _settings;

        public SettingsFactory(Dictionary<string, string?> settings) => _settings = settings;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        }
    }

    [Theory]
    [InlineData("RateLimiting:Login:PermitLimit", "0", "RateLimiting:Login:PermitLimit")]
    [InlineData("RateLimiting:Checkout:WindowSeconds", "0", "RateLimiting:Checkout:WindowSeconds")]
    [InlineData("RateLimiting:PaymentVerify:PermitLimit", "0", "RateLimiting:PaymentVerify:PermitLimit")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "0.0.0.0/0", "ForwardedHeaders:KnownNetworks")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "172.18.0.1/16", "ForwardedHeaders:KnownNetworks")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip", "ForwardedHeaders:KnownProxies")]
    [InlineData("ForwardedHeaders:ForwardLimit", "0", "ForwardedHeaders:ForwardLimit")]
    public void InvalidSettings_FailStartup(string key, string value, string expectedInMessage)
    {
        using var factory = new SettingsFactory(new Dictionary<string, string?> { [key] = value });

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage($"*{expectedInMessage}*");
    }

    [Fact]
    public void EmptyProxyEntries_FromUnsetComposeVariables_AreIgnored_AndStartupSucceeds()
    {
        using var factory = new SettingsFactory(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "",
            ["ForwardedHeaders:KnownNetworks:0"] = ""
        });

        var act = () => factory.CreateClient();

        act.Should().NotThrow();
    }
}
