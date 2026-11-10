using BusinessLogic.DTOs.Auth;

namespace Online_Store_Application.Extensions;

/// <summary>
/// F-20: IP و User-Agent کلاینت از خود اتصال. چون UseForwardedHeaders اولین middleware است،
/// <c>RemoteIpAddress</c> پشت پروکسی مورد اعتماد همان IP واقعی کلاینت است و در غیر این صورت آدرس اتصال TCP.
/// </summary>
public static class HttpContextClientExtensions
{
    // هم‌اندازهٔ ستون‌های UserSession (AppDbContext).
    private const int MaxIpAddressLength = 45;
    private const int MaxUserAgentLength = 500;

    /// <summary>IP حل‌شدهٔ کلاینت؛ آدرس IPv4 نگاشت‌شده به IPv6 به شکل IPv4 برگردانده می‌شود.</summary>
    public static string? GetClientIpAddress(this HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
            return null;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return address.ToString();
    }

    public static ClientRequestInfo GetClientRequestInfo(this HttpContext context) =>
        new(
            Truncate(context.GetClientIpAddress(), MaxIpAddressLength),
            Truncate(context.Request.Headers.UserAgent.ToString(), MaxUserAgentLength));

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
