using Application.Interfaces;
using Application.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Batch 46 (N9، review §9.3 قاعدهٔ ۶): فرستندهٔ پیامک برای Development، Testing و Staging — چیزی نمی‌فرستد و فقط لاگ می‌کند.
/// در Production راه‌اندازی با این فرستنده شکست می‌خورد (<see cref="IsRealProvider"/> = false)؛ اگر با این حال اینجا برسد،
/// متن پیامک (حاوی کد) هرگز لاگ نمی‌شود و شماره پوشیده است.
/// </summary>
public sealed class LoggingSmsSender : ISmsSender
{
    private readonly ILogger<LoggingSmsSender> _logger;
    private readonly bool _mayLogText;

    public LoggingSmsSender(ILogger<LoggingSmsSender> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _mayLogText = !ProductionConfigurationRules.IsProduction(environment.EnvironmentName);
    }

    public bool IsRealProvider => false;

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        if (_mayLogText)
        {
            _logger.LogInformation("[SMS not sent: logging sender] To {PhoneNumber}: {Text}", message.PhoneNumber, message.Text);
        }
        else
        {
            _logger.LogError(
                "SMS to {PhoneNumber} was NOT delivered: no real SMS provider is configured (Sms:Provider).",
                OtpService.IranianPhoneMask(message.PhoneNumber));
        }

        return Task.CompletedTask;
    }
}
