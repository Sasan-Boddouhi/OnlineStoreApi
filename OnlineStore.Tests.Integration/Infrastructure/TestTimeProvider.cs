namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// Phase 9 (R44) — ساعت قابل کنترل برای تست job پاک‌سازی.
/// بدون این، تست باید ۳۰ دقیقه واقعی صبر کند یا هیچ چیز واقعی را نسنجد.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset start) => _utcNow = start;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
}