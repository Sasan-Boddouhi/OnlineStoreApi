using System.Security.Cryptography;
using System.Text;
using Application.Interfaces;
using Application.Options;
using Microsoft.Extensions.Options;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 8 — اعتبارسنجی امضای webhook با HMAC-SHA256.
///
/// اگر راز تنظیم نشده باشد، هیچ امضایی پذیرفته نمی‌شود (fail-closed).
/// مقایسه با FixedTimeEquals انجام می‌شود تا حملهٔ زمان‌سنجی ممکن نباشد.
/// </summary>
public sealed class PaymentSignatureValidator : IPaymentSignatureValidator
{
    private readonly PaymentGatewaySettings _settings;

    public PaymentSignatureValidator(IOptions<PaymentGatewaySettings> settings)
    {
        _settings = settings.Value;
    }

    public string Sign(string payload)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebhookSecret))
            throw new InvalidOperationException("WebhookSecret is not configured.");

        var key = Encoding.UTF8.GetBytes(_settings.WebhookSecret);
        var data = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(data)).ToLowerInvariant();
    }

    public bool Verify(string payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebhookSecret))
            return false;

        if (string.IsNullOrWhiteSpace(signature))
            return false;

        string expected;
        try
        {
            expected = Sign(payload);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        // طول متفاوت یعنی امضای نامعتبر، بدون افشای اطلاعات.
        if (expected.Length != signature.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
    }
}