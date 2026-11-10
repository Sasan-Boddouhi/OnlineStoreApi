namespace BusinessLogic.DTOs.Auth;

/// <summary>
/// F-20: اطلاعات اتصال کلاینت که لایهٔ API از خود درخواست HTTP می‌خواند (IP حل‌شده پس از
/// پروکسی‌های مورد اعتماد و هدر User-Agent)، نه از بدنهٔ درخواست که کلاینت می‌تواند هر چیزی در آن بنویسد.
/// </summary>
public sealed record ClientRequestInfo(string? IpAddress, string? UserAgent);
