using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Common.Helpers;
using Application.Interfaces;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// Batch 46: فرستندهٔ پیامک تست. پیامک‌ها را نگه می‌دارد تا تست کد را بخواند (کد فقط در متن پیامک است؛ پایگاه داده فقط HMAC دارد).
/// </summary>
public sealed class CapturingSmsSender : ISmsSender
{
    private static readonly Regex CodePattern = new(@"\b(\d{6})\b", RegexOptions.Compiled);

    private readonly ConcurrentQueue<SmsMessage> _messages = new();

    /// <summary>تست‌های Production این را true می‌کنند تا جای آداپتور واقعی (Batch 46b) را بگیرد.</summary>
    public bool IsRealProvider { get; set; }

    /// <summary>اگر تنظیم شود، ارسال با این استثنا شکست می‌خورد (خطای ارائه‌دهنده).</summary>
    public Exception? FailWith { get; set; }

    public IReadOnlyList<SmsMessage> Messages => _messages.ToArray();

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
            throw FailWith;

        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<SmsMessage> MessagesTo(string canonicalPhone) =>
        Messages.Where(m => m.PhoneNumber == canonicalPhone).ToList();

    /// <summary>کد آخرین پیامک به این شماره (شکل استاندارد).</summary>
    public string LastCodeFor(string canonicalPhone)
    {
        var last = MessagesTo(canonicalPhone).LastOrDefault()
            ?? throw new InvalidOperationException($"No SMS was sent to {canonicalPhone}.");
        return CodePattern.Match(last.Text).Groups[1].Value;
    }

    public void Reset()
    {
        _messages.Clear();
        FailWith = null;
    }
}

/// <summary>Batch 46: ثبت‌نام دو مرحله‌ای در تست‌هایی که فقط یک کاربر تازه لازم دارند.</summary>
public static class PhoneVerificationTestExtensions
{
    /// <summary>کد را از راه سرویس می‌فرستد، از پیامک ضبط‌شده می‌خواند، تأیید می‌کند و توکن تأیید ثبت‌نام را برمی‌گرداند.</summary>
    public static async Task<string> IssueRegisterVerificationTokenAsync<TProgram>(
        this IntegrationTestFactory<TProgram> factory, string phone) where TProgram : class
    {
        var canonical = IranianPhoneNumber.NormalizeOrThrow(phone);

        await using var scope = factory.Services.CreateAsyncScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();

        await otp.SendAsync(new OtpSendDto { PhoneNumber = canonical }, "127.0.0.1");
        var code = factory.Sms.LastCodeFor(canonical);
        var verified = await otp.VerifyAsync(new OtpVerifyDto { PhoneNumber = canonical, Code = code });

        return verified.VerificationToken;
    }

    /// <summary>
    /// همان بدنهٔ ثبت‌نام قبلی، به‌علاوهٔ <c>phoneVerificationToken</c> برای شمارهٔ بدنه (اگر شماره معتبر باشد؛
    /// شمارهٔ نامعتبر بدون توکن فرستاده می‌شود تا اعتبارسنجی همان 422 قبلی را بدهد).
    /// </summary>
    public static async Task<HttpResponseMessage> PostRegisterWithVerifiedPhoneAsync<TProgram>(
        this HttpClient client,
        IntegrationTestFactory<TProgram> factory,
        object body,
        Action<HttpRequestMessage>? configure = null) where TProgram : class
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        var phoneKey = json.Select(kv => kv.Key)
            .FirstOrDefault(k => string.Equals(k, "phoneNumber", StringComparison.OrdinalIgnoreCase));
        var phone = phoneKey is null ? null : json[phoneKey]?.GetValue<string>();

        if (IranianPhoneNumber.IsValid(phone))
            json["phoneVerificationToken"] = await factory.IssueRegisterVerificationTokenAsync(phone);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register") { Content = JsonContent.Create(json) };
        configure?.Invoke(request);
        return await client.SendAsync(request);
    }
}
