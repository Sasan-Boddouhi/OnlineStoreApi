using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Entities;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Online_Store_Application.Services;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>Batch 46: میزبان تست با سقف‌های واقعی OTP (همان پیش‌فرض‌های appsettings.json).</summary>
public sealed class OtpTestFactory : IntegrationTestFactory<Program>
{
    public const int CooldownSeconds = 120;
    public const int CodeLifetimeSeconds = 120;
    public const int MaxAttempts = 5;
    public const int PerHour = 5;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Otp:ResendCooldownSeconds"] = CooldownSeconds.ToString(),
            ["Otp:CodeLifetimeSeconds"] = CodeLifetimeSeconds.ToString(),
            ["Otp:MaxVerifyAttempts"] = MaxAttempts.ToString(),
            ["Otp:MaxSendsPerPhonePerHour"] = PerHour.ToString(),
            ["Otp:MaxSendsPerPhonePerDay"] = "10",
            ["Otp:GlobalDailySendLimit"] = "1000"
        }));
    }
}

/// <summary>
/// Batch 46 (F-12، N9، review §9.3) — تأیید شماره با OTP و ثبت‌نام دو مرحله‌ای:
/// ارسال و تأیید؛ ششمین تلاش کد را می‌کشد؛ انقضا با ساعت تست؛ ارسال دوم در فاصلهٔ cooldown → 429؛
/// پاسخ یکسان برای شمارهٔ دارای حساب و بدون حساب؛ ثبت‌نام بدون کد تأییدشده رد می‌شود؛ حساب سیستمی هرگز کد نمی‌گیرد؛
/// بوت‌استرپ SuperAdmin شمارهٔ تأییدنشده را نادیده می‌گیرد.
/// </summary>
public class AuthOtpIntegrationTests : IClassFixture<OtpTestFactory>, IAsyncLifetime
{
    private const string SendUrl = "/api/v1/auth/otp/send";
    private const string VerifyUrl = "/api/v1/auth/otp/verify";
    private const string RegisterUrl = "/api/v1/auth/register";

    private readonly OtpTestFactory _factory;
    private readonly HttpClient _client;

    public AuthOtpIntegrationTests(OtpTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------- send + verify + register ----------------

    [Fact]
    public async Task SendVerifyRegister_CompletesWithConfirmedPhone_AndStoresOnlyAHashOfTheCode()
    {
        const string phone = "09124600001";

        var send = await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = "+98 912 460 0001" });
        send.StatusCode.Should().Be(HttpStatusCode.OK);
        var sendBody = await send.Content.ReadFromJsonAsync<JsonElement>();
        sendBody.GetProperty("expiresInSeconds").GetInt32().Should().Be(OtpTestFactory.CodeLifetimeSeconds);
        sendBody.GetProperty("resendAfterSeconds").GetInt32().Should().Be(OtpTestFactory.CooldownSeconds);

        var code = _factory.Sms.LastCodeFor(phone);
        code.Should().MatchRegex("^[0-9]{6}$");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Set<OtpCode>().AsNoTracking().SingleAsync(o => o.PhoneNumber == phone);
            row.CodeHash.Should().HaveLength(64).And.NotContain(code, "only a keyed hash of the code is stored");
            row.Purpose.Should().Be(OtpPurpose.Register);
        }

        var verify = await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = phone, code });
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("verificationToken").GetString();
        token.Should().NotBeNullOrWhiteSpace();

        var register = await _client.PostAsJsonAsync(RegisterUrl, RegisterBody(phone, token));
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = (await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user");
        user.GetProperty("phoneNumberConfirmed").GetBoolean().Should().BeTrue();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.User.AsNoTracking().SingleAsync(u => u.PhoneNumber == phone)).PhoneNumberConfirmed.Should().BeTrue();
        }

        // replay: the same code and the same verification token are single-use.
        await AssertProblemAsync(await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = phone, code }),
            HttpStatusCode.UnprocessableEntity, "OTP_INVALID");
        await AssertProblemAsync(await _client.PostAsJsonAsync(RegisterUrl, RegisterBody(phone, token)),
            HttpStatusCode.UnprocessableEntity, "PHONE_NOT_VERIFIED");
    }

    [Fact]
    public async Task Register_WithoutVerifiedCode_IsRejected_AndNoUserIsCreated()
    {
        const string phone = "09124600002";

        await AssertProblemAsync(await _client.PostAsJsonAsync(RegisterUrl, RegisterBody(phone, token: null)),
            HttpStatusCode.UnprocessableEntity, "PHONE_NOT_VERIFIED");
        await AssertProblemAsync(await _client.PostAsJsonAsync(RegisterUrl, RegisterBody(phone, token: "made-up-token")),
            HttpStatusCode.UnprocessableEntity, "PHONE_NOT_VERIFIED");

        // a token verified for another phone does not register this one.
        var otherToken = await _factory.IssueRegisterVerificationTokenAsync("09124600003");
        await AssertProblemAsync(await _client.PostAsJsonAsync(RegisterUrl, RegisterBody(phone, otherToken)),
            HttpStatusCode.UnprocessableEntity, "PHONE_NOT_VERIFIED");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.User.AnyAsync(u => u.PhoneNumber == phone)).Should().BeFalse();
    }

    [Fact]
    public async Task Register_AfterVerification_ForAnExistingAccount_Answers409_OnlyToThePhoneOwner()
    {
        // seeded user: only someone who received the SMS can learn that the account exists.
        var token = await _factory.IssueRegisterVerificationTokenAsync("09123456789");

        await AssertProblemAsync(await _client.PostAsJsonAsync(RegisterUrl, RegisterBody("09123456789", token)),
            HttpStatusCode.Conflict, "USER_PHONE_EXISTS");
    }

    // ---------------- brute force ----------------

    [Fact]
    public async Task Verify_SixthAttemptAfterFiveWrongCodes_KillsTheCode()
    {
        const string phone = "09124600010";
        (await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = phone })).EnsureSuccessStatusCode();
        var code = _factory.Sms.LastCodeFor(phone);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < OtpTestFactory.MaxAttempts; i++)
            await AssertProblemAsync(await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = phone, code = wrong }),
                HttpStatusCode.UnprocessableEntity, "OTP_INVALID");

        var sixth = await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = phone, code });
        await AssertProblemAsync(sixth, HttpStatusCode.TooManyRequests, "OTP_TOO_MANY_ATTEMPTS");
        sixth.Headers.RetryAfter.Should().NotBeNull();

        await AssertProblemAsync(await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = phone, code }),
            HttpStatusCode.TooManyRequests, "OTP_TOO_MANY_ATTEMPTS");
    }

    [Fact]
    public async Task Verify_AfterTheCodeLifetime_ReturnsOtpExpired()
    {
        const string phone = "09124600020";
        (await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = phone })).EnsureSuccessStatusCode();
        var code = _factory.Sms.LastCodeFor(phone);

        _factory.Clock.Advance(TimeSpan.FromSeconds(OtpTestFactory.CodeLifetimeSeconds + 1));

        await AssertProblemAsync(await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = phone, code }),
            HttpStatusCode.UnprocessableEntity, "OTP_EXPIRED");
    }

    [Fact]
    public async Task Verify_PersianDigitsInTheCode_AreAccepted()
    {
        const string phone = "09124600021";
        (await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = phone })).EnsureSuccessStatusCode();
        var persian = new string(_factory.Sms.LastCodeFor(phone).Select(c => (char)('۰' + (c - '0'))).ToArray());

        (await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = phone, code = persian }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------- SMS pumping ----------------

    [Fact]
    public async Task Send_SecondInsideTheCooldown_Returns429WithRetryAfter_AndANewCodeReplacesTheOldOne()
    {
        const string phone = "09124600030";
        (await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = phone })).EnsureSuccessStatusCode();
        var firstCode = _factory.Sms.LastCodeFor(phone);

        var second = await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = "9124600030" });
        await AssertProblemAsync(second, HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
        second.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, OtpTestFactory.CooldownSeconds);
        _factory.Sms.MessagesTo(phone).Should().HaveCount(1, "no SMS inside the cooldown");

        _factory.Clock.Advance(TimeSpan.FromSeconds(OtpTestFactory.CooldownSeconds + 1));
        (await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = phone })).EnsureSuccessStatusCode();
        var secondCode = _factory.Sms.LastCodeFor(phone);

        if (secondCode != firstCode)
            await AssertProblemAsync(await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = phone, code = firstCode }),
                HttpStatusCode.UnprocessableEntity, "OTP_INVALID");
        (await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = phone, code = secondCode }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "one active code per phone and purpose: the newest");
    }

    [Fact]
    public async Task Send_MoreThanTheHourlyLimitForOnePhone_Returns429()
    {
        const string phone = "09124600040";
        for (var i = 0; i < OtpTestFactory.PerHour; i++)
        {
            (await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = phone })).StatusCode.Should().Be(HttpStatusCode.OK);
            _factory.Clock.Advance(TimeSpan.FromSeconds(OtpTestFactory.CooldownSeconds + 1));
        }

        await AssertProblemAsync(await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = phone }),
            HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Send_NonIranianMobile_Returns422_AndSendsNothing()
    {
        var response = await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = "02112345678" });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        _factory.Sms.Messages.Should().BeEmpty();
    }

    // ---------------- enumeration ----------------

    [Fact]
    public async Task Send_KnownAndUnknownPhone_GetTheSameStatusAndBody()
    {
        var known = await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = "09123456789" });   // seeded account
        var unknown = await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = "09124600050" });

        known.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(known.StatusCode);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await known.Content.ReadAsStringAsync());
    }

    // ---------------- system account (owner answer for Batch 49b, item 1) ----------------

    [Fact]
    public async Task SystemAccountPhone_NeverGetsACode_AndCannotVerifyOrRegister()
    {
        const string systemPhone = "09124600060";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.User.Add(new User
            {
                FirstName = "System",
                LastName = "Account",
                PhoneNumber = systemPhone,
                PasswordHash = "!",
                UserType = UserType.Employee,
                IsActive = false,
                IsSystem = true
            });
            await db.SaveChangesAsync();
        }

        var send = await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = systemPhone });
        var unknown = await _client.PostAsJsonAsync(SendUrl, new { phoneNumber = "09124600061" });
        send.StatusCode.Should().Be(HttpStatusCode.OK, "same generic answer as any other phone");
        (await send.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
        _factory.Sms.MessagesTo(systemPhone).Should().BeEmpty("a system account never receives an OTP");

        await AssertProblemAsync(await _client.PostAsJsonAsync(VerifyUrl, new { phoneNumber = systemPhone, code = "123456" }),
            HttpStatusCode.UnprocessableEntity, "OTP_INVALID");
        await AssertProblemAsync(await _client.PostAsJsonAsync(RegisterUrl, RegisterBody(systemPhone, token: null)),
            HttpStatusCode.UnprocessableEntity, "PHONE_NOT_VERIFIED");
    }

    // ---------------- SuperAdmin bootstrap ----------------

    [Fact]
    public async Task SuperAdminBootstrap_IgnoresAnUnconfirmedPhone()
    {
        const string phone = "09124600070";
        await AddUserAsync(phone, confirmed: false);

        await RunBootstrapperAsync(phone);

        (await SuperAdminCountAsync()).Should().Be(0, "SuperAdmin bootstrap only for a confirmed phone (review §9.3 rule 5)");
    }

    [Fact]
    public async Task SuperAdminBootstrap_PromotesAConfirmedPhone()
    {
        const string phone = "09124600071";
        await AddUserAsync(phone, confirmed: true);

        // directive 2: the configured phone goes through the shared normaliser too.
        await RunBootstrapperAsync("+98 912 460 0071");

        (await SuperAdminCountAsync()).Should().Be(1);
    }

    // ---------------- helpers ----------------

    private static object RegisterBody(string phone, string? token) => new
    {
        phoneNumber = phone,
        password = "Test@1234",
        firstName = "کاربر",
        lastName = "OTP",
        deviceId = "otp-test",
        phoneVerificationToken = token
    };

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().Should().Be(code, body);
    }

    private async Task AddUserAsync(string phone, bool confirmed)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.User.Add(new User
        {
            FirstName = "Boot",
            LastName = "Strap",
            PhoneNumber = phone,
            PasswordHash = "hash",
            UserType = UserType.Customer,
            IsActive = true,
            PhoneNumberConfirmed = confirmed
        });
        await db.SaveChangesAsync();
    }

    private async Task RunBootstrapperAsync(string configuredPhone)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BOOTSTRAP_SUPERADMIN_PHONE"] = configuredPhone })
            .Build();

        using var bootstrapper = new SuperAdminBootstrapper(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<SuperAdminBootstrapper>.Instance);

        await bootstrapper.StartAsync(CancellationToken.None);
        await bootstrapper.ExecuteTask!;
    }

    private async Task<int> SuperAdminCountAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserRole.CountAsync(ur => ur.Role!.Code == "SuperAdmin");
    }
}
