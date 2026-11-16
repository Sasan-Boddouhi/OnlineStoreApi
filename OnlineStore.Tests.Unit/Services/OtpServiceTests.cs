using System.Text.RegularExpressions;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.Services.Implementations;
using DataLayer.Context;
using DataLayer.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using OnlineStore.Tests.Shared.BackgroundProcessing;

namespace OnlineStore.Tests.Unit.Services;

/// <summary>Batch 46 (review §9.3) — OtpService: hashing, single use, attempts, expiry, limits, budget, system account.</summary>
public class OtpServiceTests : IDisposable
{
    private const string Phone = "09124610001";

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingSms _sms = new();
    private readonly RecordingLogger<OtpService> _logger = new();
    private readonly OtpOptions _options = new()
    {
        HmacKey = "unit-test-otp-hmac-key-0123456789abcdef",
        CodeLifetimeSeconds = 120,
        MaxVerifyAttempts = 5,
        ResendCooldownSeconds = 120,
        MaxSendsPerPhonePerHour = 5,
        MaxSendsPerPhonePerDay = 10,
        GlobalDailySendLimit = 1000,
        VerificationTokenLifetimeSeconds = 900
    };

    private readonly List<AppDbContext> _contexts = new();

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new AppDbContext(options, Enumerable.Empty<ISaveChangesInterceptor>());
        _contexts.Add(context);
        return context;
    }

    private OtpService NewService(AppDbContext? context = null) =>
        new(new UnitOfWork(context ?? NewContext()), _sms, _clock, Microsoft.Extensions.Options.Options.Create(_options), _logger);

    public void Dispose()
    {
        foreach (var context in _contexts)
            context.Dispose();
    }

    private async Task<string> SendAndReadCodeAsync(string phone = Phone)
    {
        await NewService().SendAsync(new OtpSendDto { PhoneNumber = phone }, "203.0.113.1");
        return _sms.LastCode(phone);
    }

    // ---------------- storage ----------------

    [Fact]
    public async Task Send_StoresOnlyAKeyedHash_BoundToPhoneAndPurpose()
    {
        var code = await SendAndReadCodeAsync();
        _clock.Advance(TimeSpan.FromSeconds(121));
        await SendAndReadCodeAsync("09124610002");

        var rows = await NewContext().OtpCode.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => Regex.IsMatch(r.CodeHash, "^[0-9A-F]{64}$"));
        rows.Should().NotContain(r => r.CodeHash.Contains(code));
        rows.Select(r => r.CodeHash).Should().OnlyHaveUniqueItems();
        rows.Single(r => r.PhoneNumber == Phone).RequestIp.Should().Be("203.0.113.1");
    }

    [Fact]
    public async Task Send_NormalisesThePhone_WithTheSharedNormaliser()
    {
        await NewService().SendAsync(new OtpSendDto { PhoneNumber = "+98 ۹۱۲ ۴۶۱ ۰۰۰۱" }, null);

        _sms.Messages.Should().ContainSingle().Which.PhoneNumber.Should().Be(Phone);
        (await NewContext().OtpCode.SingleAsync()).PhoneNumber.Should().Be(Phone);
    }

    [Fact]
    public async Task Send_ANonIranianMobile_Throws422_AndSendsNothing()
    {
        var act = () => NewService().SendAsync(new OtpSendDto { PhoneNumber = "+44 7700 900123" }, null);

        (await act.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("INVALID_PHONE_NUMBER");
        _sms.Messages.Should().BeEmpty();
    }

    // ---------------- verify ----------------

    [Fact]
    public async Task Verify_CorrectCode_ReturnsASingleUseToken_ThatOnlyThisPhoneAndPurposeCanConsume()
    {
        var code = await SendAndReadCodeAsync();

        var result = await NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = code });
        result.VerificationToken.Should().NotBeNullOrWhiteSpace();
        result.ExpiresInSeconds.Should().Be(900);

        var otherPhone = () => NewService().ConsumeVerificationAsync("09124610009", OtpPurpose.Register, result.VerificationToken);
        (await otherPhone.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("PHONE_NOT_VERIFIED");

        await NewService().ConsumeVerificationAsync(Phone, OtpPurpose.Register, result.VerificationToken);

        var replay = () => NewService().ConsumeVerificationAsync(Phone, OtpPurpose.Register, result.VerificationToken);
        (await replay.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("PHONE_NOT_VERIFIED");

        var reverify = () => NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = code });
        (await reverify.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("OTP_INVALID");
    }

    [Fact]
    public async Task Verify_StoresOnlyAHashOfTheVerificationToken()
    {
        var code = await SendAndReadCodeAsync();
        var result = await NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = code });

        var row = await NewContext().OtpCode.AsNoTracking().SingleAsync();
        row.VerificationTokenHash.Should().MatchRegex("^[0-9A-F]{64}$").And.NotBe(result.VerificationToken);
    }

    [Fact]
    public async Task Verify_FiveWrongCodes_KillTheCode_TheSixthAttemptIsTooManyAttempts()
    {
        var code = await SendAndReadCodeAsync();
        var wrong = code == "999999" ? "000000" : "999999";

        for (var i = 0; i < _options.MaxVerifyAttempts; i++)
        {
            var attempt = () => NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = wrong });
            (await attempt.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("OTP_INVALID");
        }

        var sixth = () => NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = code });
        var thrown = (await sixth.Should().ThrowAsync<TooManyRequestsException>()).Which;
        thrown.Code.Should().Be("OTP_TOO_MANY_ATTEMPTS");
        thrown.StatusCode.Should().Be(429);

        (await NewContext().OtpCode.SingleAsync()).AttemptCount.Should().Be(_options.MaxVerifyAttempts, "a dead code takes no more attempts");
    }

    [Fact]
    public async Task Verify_AfterTheLifetime_IsExpired()
    {
        var code = await SendAndReadCodeAsync();
        _clock.Advance(TimeSpan.FromSeconds(_options.CodeLifetimeSeconds));

        var act = () => NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = code });

        (await act.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("OTP_EXPIRED");
    }

    [Fact]
    public async Task Verify_WithoutAnyCodeForThePhone_IsInvalid()
    {
        var act = () => NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = "123456" });

        (await act.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("OTP_INVALID");
    }

    [Fact]
    public async Task Verify_TwoConcurrentAttemptsOnTheSameCode_DoNotBothCount()
    {
        var code = await SendAndReadCodeAsync();
        var wrong = code == "999999" ? "000000" : "999999";

        // both requests read the row before either saves (optimistic concurrency on Version).
        var first = NewContext();
        var second = NewContext();
        await first.OtpCode.LoadAsync();
        await second.OtpCode.LoadAsync();

        var a = () => NewService(first).VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = wrong });
        var b = () => NewService(second).VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = code });
        await a.Should().ThrowAsync<UnprocessableEntityException>();
        (await b.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("OTP_INVALID",
            "the loser of the race gets a generic invalid answer instead of a free attempt");

        (await NewContext().OtpCode.SingleAsync()).AttemptCount.Should().Be(1);
    }

    // ---------------- send limits ----------------

    [Fact]
    public async Task Send_InsideTheCooldown_Is429WithRetryAfter_AndANewSendReplacesTheActiveCode()
    {
        var first = await SendAndReadCodeAsync();
        _clock.Advance(TimeSpan.FromSeconds(30));

        var act = () => NewService().SendAsync(new OtpSendDto { PhoneNumber = Phone }, null);
        var thrown = (await act.Should().ThrowAsync<TooManyRequestsException>()).Which;
        thrown.Code.Should().Be("RATE_LIMIT_EXCEEDED");
        thrown.RetryAfterSeconds.Should().Be(90);

        _clock.Advance(TimeSpan.FromSeconds(90));
        var second = await SendAndReadCodeAsync();

        var rows = await NewContext().OtpCode.AsNoTracking().OrderBy(r => r.OtpCodeId).ToListAsync();
        rows[0].ConsumedAtUtc.Should().NotBeNull("one active code per phone and purpose");
        rows[1].ConsumedAtUtc.Should().BeNull();

        if (first != second)
        {
            var old = () => NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = first });
            (await old.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("OTP_INVALID");
        }
    }

    [Fact]
    public async Task Send_TheDailyLimitPerPhone_Is429_UntilTheOldestSendIsADayOld()
    {
        // 400 s apart: never more than 9 sends in one hour, so only the daily limit (10) can apply.
        _options.MaxSendsPerPhonePerHour = 10;
        for (var i = 0; i < _options.MaxSendsPerPhonePerDay; i++)
        {
            await SendAndReadCodeAsync();
            _clock.Advance(TimeSpan.FromSeconds(400));
        }

        var act = () => NewService().SendAsync(new OtpSendDto { PhoneNumber = Phone }, null);
        var thrown = (await act.Should().ThrowAsync<TooManyRequestsException>()).Which;
        thrown.Code.Should().Be("RATE_LIMIT_EXCEEDED");
        thrown.RetryAfterSeconds.Should().Be(86400 - 4000, "the first send of the window leaves it after 24 h");
    }

    [Fact]
    public async Task Send_WhenTheGlobalDailyBudgetIsUsed_Is503_AndRaisesAnErrorLog()
    {
        _options.GlobalDailySendLimit = 2;
        await SendAndReadCodeAsync("09124610011");
        await SendAndReadCodeAsync("09124610012");

        var act = () => NewService().SendAsync(new OtpSendDto { PhoneNumber = "09124610013" }, null);

        var thrown = (await act.Should().ThrowAsync<ServiceUnavailableException>()).Which;
        thrown.Code.Should().Be("OTP_SEND_UNAVAILABLE");
        thrown.StatusCode.Should().Be(503);
        thrown.RetryAfterSeconds.Should().Be(14 * 3600, "until 00:00 UTC");
        _sms.Messages.Should().HaveCount(2);
        _logger.Messages.Should().Contain(m => m.Contains("global daily SMS budget"));
    }

    [Fact]
    public async Task Send_WhenTheProviderFails_Is503_AndTheStoredCodeCannotBeUsed()
    {
        _sms.FailWith = new HttpRequestException("provider down");

        var act = () => NewService().SendAsync(new OtpSendDto { PhoneNumber = Phone }, null);

        (await act.Should().ThrowAsync<ServiceUnavailableException>()).Which.Code.Should().Be("OTP_SEND_UNAVAILABLE");
        (await NewContext().OtpCode.SingleAsync()).ConsumedAtUtc.Should().NotBeNull();
    }

    // ---------------- system account (owner answer for Batch 49b) ----------------

    [Fact]
    public async Task SystemAccount_NeverGetsACode_AndVerifyIsInvalid()
    {
        var context = NewContext();
        context.User.Add(new User
        {
            FirstName = "System",
            LastName = "Account",
            PhoneNumber = Phone,
            PasswordHash = "!",
            UserType = UserType.Employee,
            IsActive = false,
            IsSystem = true
        });
        await context.SaveChangesAsync();

        var result = await NewService().SendAsync(new OtpSendDto { PhoneNumber = Phone }, null);

        result.ExpiresInSeconds.Should().Be(_options.CodeLifetimeSeconds, "same generic answer");
        _sms.Messages.Should().BeEmpty();
        (await NewContext().OtpCode.AnyAsync()).Should().BeFalse();

        var verify = () => NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = "123456" });
        (await verify.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("OTP_INVALID");
    }

    // ---------------- token ----------------

    [Fact]
    public async Task ConsumeVerification_AfterTheTokenLifetime_IsNotVerified()
    {
        var code = await SendAndReadCodeAsync();
        var result = await NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = code });
        _clock.Advance(TimeSpan.FromSeconds(_options.VerificationTokenLifetimeSeconds));

        var act = () => NewService().ConsumeVerificationAsync(Phone, OtpPurpose.Register, result.VerificationToken);

        (await act.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("PHONE_NOT_VERIFIED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ConsumeVerification_WithoutAToken_IsNotVerified(string? token)
    {
        var act = () => NewService().ConsumeVerificationAsync(Phone, OtpPurpose.Register, token);

        (await act.Should().ThrowAsync<UnprocessableEntityException>()).Which.Code.Should().Be("PHONE_NOT_VERIFIED");
    }

    // ---------------- logging ----------------

    [Fact]
    public async Task NeitherTheCodeNorTheTokenIsEverLogged()
    {
        var code = await SendAndReadCodeAsync();
        var result = await NewService().VerifyAsync(new OtpVerifyDto { PhoneNumber = Phone, Code = code });

        _logger.Messages.Should().NotContain(m => m.Contains(code) || m.Contains(result.VerificationToken));
        _logger.Messages.Should().NotContain(m => m.Contains(Phone), "phones are masked in logs");
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    [InlineData("Staging", true)]
    public async Task LoggingSmsSender_NeverLogsTheTextInProduction(string environment, bool textLogged)
    {
        var logger = new RecordingLogger<LoggingSmsSender>();
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(environment);
        var sender = new LoggingSmsSender(logger, host.Object);

        await sender.SendAsync(new SmsMessage(Phone, "کد تأیید شما: 482913"));

        sender.IsRealProvider.Should().BeFalse();
        logger.Messages.Any(m => m.Contains("482913")).Should().Be(textLogged);
        if (!textLogged)
            logger.Messages.Should().NotContain(m => m.Contains(Phone));
    }

    // ---------------- options ----------------

    [Fact]
    public void Options_InProduction_RequireAKeyAndARealSmsProvider()
    {
        var errors = new OtpOptions().Validate("Production", smsSenderIsReal: false);

        errors.Should().Contain(e => e.Contains("Otp:HmacKey"));
        errors.Should().Contain(e => e.Contains("Sms:Provider"));
        new OtpOptions { HmacKey = new string('k', 32) }.Validate("Production", smsSenderIsReal: true).Should().BeEmpty();
        new OtpOptions().Validate("Development", smsSenderIsReal: false).Should().BeEmpty();
    }

    private sealed class RecordingSms : ISmsSender
    {
        private readonly List<SmsMessage> _messages = new();

        public bool IsRealProvider => true;

        public Exception? FailWith { get; set; }

        public IReadOnlyList<SmsMessage> Messages => _messages;

        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
        {
            if (FailWith is not null)
                throw FailWith;
            _messages.Add(message);
            return Task.CompletedTask;
        }

        public string LastCode(string phone) =>
            Regex.Match(_messages.Last(m => m.PhoneNumber == phone).Text, @"\d{6}").Value;
    }
}
