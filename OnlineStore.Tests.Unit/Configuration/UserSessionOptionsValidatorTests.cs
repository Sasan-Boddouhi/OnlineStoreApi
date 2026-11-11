using Application.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Online_Store_Application.Extensions;

namespace OnlineStore.Tests.Unit.Configuration;

/// <summary>
/// Batch 11 (F-13): سیاست نشست هنگام راه‌اندازی سنجیده می‌شود. مهم‌ترین قید: بی‌کاری باید بیشتر از
/// عمر access token باشد، وگرنه نخستین refresh پس از انقضای access token همیشه رد می‌شود.
/// </summary>
public class UserSessionOptionsValidatorTests
{
    [Fact]
    public void Defaults_AreSevenDaysIdle_ThirtyDaysAbsolute_FifteenMinuteAccessToken()
    {
        var options = Resolve(new Dictionary<string, string?>());

        options.IdleTimeout.Should().Be(TimeSpan.FromDays(7));
        options.AbsoluteLifetime.Should().Be(TimeSpan.FromDays(30));
        options.RefreshReuseGracePeriod.Should().Be(TimeSpan.FromSeconds(10));
        new JwtOptions().ExpireMinutes.Should().Be(15);
    }

    [Fact]
    public void IdleTimeout_NotGreaterThanAccessTokenLifetime_FailsValidation()
    {
        // ۱ روز بی‌کاری در برابر access token یک‌روزه (۱۴۴۰ دقیقه).
        var act = () => Resolve(new Dictionary<string, string?>
        {
            ["Jwt:ExpireMinutes"] = "1440",
            ["Session:IdleTimeoutDays"] = "1"
        });

        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(f => f.Contains("Session:IdleTimeoutDays"));
    }

    [Fact]
    public void AbsoluteLifetime_ShorterThanIdleTimeout_FailsValidation()
    {
        var act = () => Resolve(new Dictionary<string, string?>
        {
            ["Session:IdleTimeoutDays"] = "14",
            ["Session:AbsoluteLifetimeDays"] = "7"
        });

        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(f => f.Contains("Session:AbsoluteLifetimeDays"));
    }

    [Theory]
    [InlineData("Session:IdleTimeoutDays", "0")]
    [InlineData("Session:AbsoluteLifetimeDays", "0")]
    [InlineData("Session:RefreshReuseGraceSeconds", "-1")]
    [InlineData("Jwt:ExpireMinutes", "0")]
    public void OutOfRangeValues_FailValidation(string key, string value)
    {
        var act = () => Resolve(new Dictionary<string, string?> { [key] = value });

        act.Should().Throw<OptionsValidationException>();
    }

    private static UserSessionOptions Resolve(Dictionary<string, string?> settings)
    {
        settings.TryAdd("Jwt:Key", new string('k', 40));
        // Batch 40 (SEC-08): Issuer و Audience هم الزامی‌اند؛ پیکربندی پایهٔ معتبر باید آن‌ها را داشته باشد.
        settings.TryAdd("Jwt:Issuer", "OnlineStoreApi");
        settings.TryAdd("Jwt:Audience", "OnlineStoreClient");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddUserSessionOptions(configuration);
        using var provider = services.BuildServiceProvider();

        // JwtOptions اول خوانده می‌شود تا خطای DataAnnotations آن هم دیده شود.
        _ = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
        return provider.GetRequiredService<IOptions<UserSessionOptions>>().Value;
    }
}
