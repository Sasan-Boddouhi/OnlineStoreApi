using System.Collections.Concurrent;
using Application.Interfaces;
using BusinessLogic.Services.Implementations;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// Batch 5 — درگاه تست قابل برنامه‌ریزی. به‌طور پیش‌فرض همان <see cref="FakePaymentGateway"/> است؛ تست‌ها می‌توانند
/// نتیجهٔ Verifyهای بعدی را صف کنند (Verified، AlreadyVerified، Rejected، TransientError)، پیش از پاسخ Verify
/// کاری انجام دهند (شبیه‌سازی رقابت) و همهٔ فراخوانی‌ها و مبالغ را بخوانند. هیچ تماس شبکه‌ای ندارد.
///
/// Batch 43 — استعلام هم قابل برنامه‌ریزی است: <see cref="SetInquiryResult"/> پاسخ ثابتِ یک Authority و
/// <see cref="EnqueueInquiryResult"/> پاسخ‌های بعدی (برای هر Authority) را تعیین می‌کند؛ بدون برنامه، پاسخ همان
/// FakePaymentGateway است (NotPaid مگر اینکه همین نمونه آن Authority را Verify کرده باشد).
/// </summary>
public sealed class ScriptedPaymentGateway : IPaymentGateway
{
    private readonly object _sync = new();
    private FakePaymentGateway _inner = new();
    private readonly ConcurrentQueue<PaymentVerificationResult> _verifyResults = new();
    private readonly ConcurrentQueue<PaymentInquiryResult> _inquiryResults = new();
    private readonly ConcurrentDictionary<string, PaymentInquiryResult> _inquiryByAuthority = new(StringComparer.Ordinal);
    private readonly List<(string Authority, decimal Amount)> _verifyCalls = new();
    private readonly List<string> _inquiryCalls = new();
    private readonly List<PaymentInitiationRequest> _initiateCalls = new();

    /// <summary>پیش از برگرداندن نتیجهٔ Verify اجرا می‌شود (مثلاً یک Verify رقیب).</summary>
    /// <summary>Batch 54: پاسخ شکست اجباری برای فراخوانی بعدی InitiateAsync (یک‌بار مصرف).</summary>
    public PaymentInitiationResult? NextInitiateFailure { get; set; }

    public Func<string, Task>? BeforeVerifyReturns { get; set; }

    /// <summary>Batch 43: پیش از برگرداندن نتیجهٔ استعلام اجرا می‌شود (مثلاً تغییر هم‌زمان پرداخت).</summary>
    public Func<string, Task>? BeforeInquiryReturns { get; set; }

    /// <summary>
    /// Batch 43 (N2): مثل درگاه واقعی با <c>PaymentGatewaySettings:EnableInquiry = false</c> رفتار می‌کند: هر استعلام
    /// <see cref="PaymentInquiryResult.Disabled"/> برمی‌گرداند. پیش‌فرض false (استعلام روشن).
    /// </summary>
    public bool InquiryTurnedOff { get; set; }

    public IReadOnlyList<(string Authority, decimal Amount)> VerifyCalls
    {
        get { lock (_sync) return _verifyCalls.ToList(); }
    }

    /// <summary>Batch 43: Authorityهایی که استعلام شده‌اند، به ترتیب.</summary>
    public IReadOnlyList<string> InquiryCalls
    {
        get { lock (_sync) return _inquiryCalls.ToList(); }
    }

    public IReadOnlyList<PaymentInitiationRequest> InitiateCalls
    {
        get { lock (_sync) return _initiateCalls.ToList(); }
    }

    public void EnqueueVerifyResult(PaymentVerificationResult result) => _verifyResults.Enqueue(result);

    /// <summary>Batch 43: پاسخ استعلامِ بعدی (هر Authority)؛ بر پاسخ ثابت مقدم است.</summary>
    public void EnqueueInquiryResult(PaymentInquiryResult result) => _inquiryResults.Enqueue(result);

    /// <summary>Batch 43: پاسخ ثابت استعلام برای یک Authority تا بازنشانی بعدی.</summary>
    public void SetInquiryResult(string authority, PaymentInquiryResult result) => _inquiryByAuthority[authority] = result;

    public void Reset()
    {
        lock (_sync)
        {
            _inner = new FakePaymentGateway();
            _verifyResults.Clear();
            _inquiryResults.Clear();
            _inquiryByAuthority.Clear();
            _verifyCalls.Clear();
            _inquiryCalls.Clear();
            _initiateCalls.Clear();
            BeforeVerifyReturns = null;
            NextInitiateFailure = null;
            BeforeInquiryReturns = null;
            InquiryTurnedOff = false;
        }
    }

    public Task<PaymentInitiationResult> InitiateAsync(PaymentInitiationRequest request, CancellationToken cancellationToken = default)
    {
        lock (_sync) _initiateCalls.Add(request);
        PaymentInitiationResult? forced;
        lock (_sync) { forced = NextInitiateFailure; NextInitiateFailure = null; }
        if (forced is not null) return Task.FromResult(forced);
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
            NextInitiateFailure = null;
            await hook(authority);
        }

        return result;
    }

    public async Task<PaymentInquiryResult> InquireAsync(string authority, CancellationToken cancellationToken = default)
    {
        lock (_sync) _inquiryCalls.Add(authority);

        PaymentInquiryResult result;
        if (InquiryTurnedOff)
            result = PaymentInquiryResult.Disabled();
        else if (_inquiryResults.TryDequeue(out var queued))
            result = queued;
        else if (_inquiryByAuthority.TryGetValue(authority, out var scripted))
            result = scripted;
        else
            result = await _inner.InquireAsync(authority, cancellationToken);

        var hook = BeforeInquiryReturns;
        if (hook is not null)
        {
            BeforeInquiryReturns = null;
            await hook(authority);
        }

        return result;
    }

    public string BuildRedirectUrl(string authority) => _inner.BuildRedirectUrl(authority);

    public Task<bool> RefundAsync(string authority, decimal amount, CancellationToken cancellationToken = default)
        => _inner.RefundAsync(authority, amount, cancellationToken);
}
