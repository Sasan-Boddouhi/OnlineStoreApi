using System.Collections.Concurrent;
using Application.Interfaces;
using BusinessLogic.Services.Implementations;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// Batch 5 — درگاه تست قابل برنامه‌ریزی. به‌طور پیش‌فرض همان <see cref="FakePaymentGateway"/> است؛ تست‌ها می‌توانند
/// نتیجهٔ Verifyهای بعدی را صف کنند (Verified، AlreadyVerified، Rejected، TransientError)، پیش از پاسخ Verify
/// کاری انجام دهند (شبیه‌سازی رقابت) و همهٔ فراخوانی‌ها و مبالغ را بخوانند. هیچ تماس شبکه‌ای ندارد.
/// </summary>
public sealed class ScriptedPaymentGateway : IPaymentGateway
{
    private readonly object _sync = new();
    private FakePaymentGateway _inner = new();
    private readonly ConcurrentQueue<PaymentVerificationResult> _verifyResults = new();
    private readonly List<(string Authority, decimal Amount)> _verifyCalls = new();
    private readonly List<PaymentInitiationRequest> _initiateCalls = new();

    /// <summary>پیش از برگرداندن نتیجهٔ Verify اجرا می‌شود (مثلاً یک Verify رقیب).</summary>
    public Func<string, Task>? BeforeVerifyReturns { get; set; }

    public IReadOnlyList<(string Authority, decimal Amount)> VerifyCalls
    {
        get { lock (_sync) return _verifyCalls.ToList(); }
    }

    public IReadOnlyList<PaymentInitiationRequest> InitiateCalls
    {
        get { lock (_sync) return _initiateCalls.ToList(); }
    }

    public void EnqueueVerifyResult(PaymentVerificationResult result) => _verifyResults.Enqueue(result);

    public void Reset()
    {
        lock (_sync)
        {
            _inner = new FakePaymentGateway();
            _verifyResults.Clear();
            _verifyCalls.Clear();
            _initiateCalls.Clear();
            BeforeVerifyReturns = null;
        }
    }

    public Task<PaymentInitiationResult> InitiateAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default)
    {
        lock (_sync) _initiateCalls.Add(request);
        return _inner.InitiateAsync(request, cancellationToken);
    }

    public async Task<PaymentVerificationResult> VerifyAsync(string authority, decimal amount, CancellationToken cancellationToken = default)
    {
        lock (_sync) _verifyCalls.Add((authority, amount));

        var result = _verifyResults.TryDequeue(out var scripted)
            ? scripted
            : await _inner.VerifyAsync(authority, amount, cancellationToken);

        var hook = BeforeVerifyReturns;
        if (hook is not null)
        {
            BeforeVerifyReturns = null;
            await hook(authority);
        }

        return result;
    }

    public string BuildRedirectUrl(string authority) => _inner.BuildRedirectUrl(authority);

    public Task<bool> RefundAsync(string authority, decimal amount, CancellationToken cancellationToken = default)
        => _inner.RefundAsync(authority, amount, cancellationToken);
}
