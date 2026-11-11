namespace BusinessLogic.Validators.Common;

/// <summary>Batch 48 (SEC-12): آدرس تصویر باید مطلق و https باشد (javascript:، data:، http: و نسبی رد می‌شوند).</summary>
public static class HttpsUrl
{
    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && !string.IsNullOrEmpty(uri.Host);
}
