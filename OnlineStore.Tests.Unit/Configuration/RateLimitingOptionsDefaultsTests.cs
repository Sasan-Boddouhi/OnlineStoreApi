using Application.Options;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Configuration;

/// <summary>
/// Batch 11b: سقف پیش‌فرض تازه‌سازی توکن ۶۰ درخواست در دقیقه برای هر IP است. توکن دسترسی ۱۵ دقیقه‌ای
/// است و کاربران موبایل زیادی پشت یک IP مشترک اپراتور (CGNAT) هستند؛ با سقف قبلی (۲۰) چند ده کاربر
/// پشت یک IP هنگام انقضای هم‌زمان توکن‌ها 429 می‌گرفتند.
/// </summary>
public class RateLimitingOptionsDefaultsTests
{
    [Fact]
    public void Refresh_DefaultsTo60PerMinute()
    {
        var options = new RateLimitingOptions();

        options.Refresh.PermitLimit.Should().Be(60);
        options.Refresh.WindowSeconds.Should().Be(60);
    }
}
