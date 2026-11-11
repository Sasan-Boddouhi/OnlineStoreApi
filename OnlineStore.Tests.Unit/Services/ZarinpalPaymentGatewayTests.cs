using System.Net;
using System.Text;
using System.Text.Json;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OnlineStore.Tests.Unit.Services;

/// <summary>
/// Batch 5 — نگاشت پاسخ‌های زرین‌پال (API v4) به نتیجهٔ مستقل از درگاه، با HttpMessageHandler جعلی و بدون شبکه.
/// مستند: https://www.zarinpal.com/docs/paymentGateway/connectToGateway و /errorList
///  • 100 ⇒ Verified، 101 ⇒ AlreadyVerified؛ ‎-50/‎-51/‎-53/‎-54 ⇒ Rejected؛
///  • 5xx، timeout، خطای شبکه، پاسخ ناخوانا و کدهای غیرقطعی ⇒ TransientError (پرداخت Failed نمی‌شود).
///  • مبلغ عدد صحیح ریال؛ currency=IRR در request؛ verify همان مبلغ را بدون currency می‌فرستد.
///  • صفحهٔ پرداخت از همان BaseUrl (سندباکس‌آگاه) ساخته می‌شود.
/// </summary>
public class ZarinpalPaymentGatewayTests
{
    private const string MerchantId = "00000000-0000-0000-0000-000000000000";

    // ==================== Verify: نگاشت کدها ====================

    [Fact]
    public async Task Verify_Code100_IsVerified_WithReference()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, """{"data":{"code":100,"message":"Verified","ref_id":201,"card_pan":"5022****1234"},"errors":[]}""");

        var result = await CreateGateway(handler).VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        result.Outcome.Should().Be(PaymentVerificationOutcome.Verified);
        result.IsCaptured.Should().BeTrue();
        result.ReferenceId.Should().Be("201");
    }

    [Fact]
    public async Task Verify_Code101_IsAlreadyVerified_AndCaptured()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, """{"data":{"code":101,"message":"Verified","ref_id":201},"errors":[]}""");

        var result = await CreateGateway(handler).VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        result.Outcome.Should().Be(PaymentVerificationOutcome.AlreadyVerified);
        result.IsCaptured.Should().BeTrue();
        result.ReferenceId.Should().Be("201");
    }

    [Theory]
    [InlineData(-50)] // مبلغ پرداخت‌شده با مبلغ verify برابر نیست
    [InlineData(-51)] // پرداخت ناموفق
    [InlineData(-53)] // پرداخت متعلق به این مرچنت نیست
    [InlineData(-54)] // Authority نامعتبر
    public async Task Verify_DefinitiveErrorCode_IsRejected(int code)
    {
        var handler = StubHandler.Json(HttpStatusCode.UnprocessableEntity,
            "{\"data\":[],\"errors\":{\"code\":" + code + ",\"message\":\"rejected\",\"validations\":[]}}");

        var result = await CreateGateway(handler).VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        result.Outcome.Should().Be(PaymentVerificationOutcome.Rejected);
        result.IsCaptured.Should().BeFalse();
    }

    [Theory]
    [InlineData(-9)]  // خطای اعتبارسنجی درخواست
    [InlineData(-10)] // IP یا مرچنت نامعتبر (پیکربندی)
    [InlineData(-12)] // تلاش بیش از حد؛ بعداً دوباره
    [InlineData(-52)] // خطای غیرمنتظره؛ تماس با پشتیبانی
    public async Task Verify_NonDefinitiveErrorCode_IsTransient_NotRejected(int code)
    {
        var handler = StubHandler.Json(HttpStatusCode.UnprocessableEntity,
            "{\"data\":[],\"errors\":{\"code\":" + code + ",\"message\":\"error\",\"validations\":[]}}");

        var result = await CreateGateway(handler).VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        result.Outcome.Should().Be(PaymentVerificationOutcome.TransientError);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Verify_Http5xx_IsTransient(HttpStatusCode status)
    {
        var handler = StubHandler.Text(status, "<html>upstream error</html>");

        var result = await CreateGateway(handler).VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        result.Outcome.Should().Be(PaymentVerificationOutcome.TransientError);
    }

    [Fact]
    public async Task Verify_Timeout_IsTransient()
    {
        var handler = StubHandler.Throws(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var result = await CreateGateway(handler).VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        result.Outcome.Should().Be(PaymentVerificationOutcome.TransientError);
    }

    [Fact]
    public async Task Verify_NetworkError_IsTransient()
    {
        var handler = StubHandler.Throws(new HttpRequestException("Connection reset by peer"));

        var result = await CreateGateway(handler).VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        result.Outcome.Should().Be(PaymentVerificationOutcome.TransientError);
    }

    [Fact]
    public async Task Verify_UnreadableBody_IsTransient()
    {
        var handler = StubHandler.Text(HttpStatusCode.OK, "not json");

        var result = await CreateGateway(handler).VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        result.Outcome.Should().Be(PaymentVerificationOutcome.TransientError);
    }

    [Fact]
    public async Task Verify_GatewayDisabled_IsTransient_SoThePaymentIsNotFailed()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, "{}");

        var result = await CreateGateway(handler, s => s.EnableRealGateway = false)
            .VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        result.Outcome.Should().Be(PaymentVerificationOutcome.TransientError);
        handler.Requests.Should().BeEmpty();
    }

    // ==================== مبلغ و واحد پول ====================

    [Fact]
    public async Task Initiate_SendsIntegerRialAmount_WithCurrencyIrr()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, """{"data":{"code":100,"message":"Success","authority":"A0000000000000000000000000000000001","fee_type":"Merchant","fee":100},"errors":[]}""");

        var result = await CreateGateway(handler).InitiateAsync(
            new PaymentInitiationRequest(42, 1_500_000m, "https://shop.example/payment/callback"));

        result.Success.Should().BeTrue();
        var request = handler.Requests.Should().ContainSingle().Which;
        request.Uri.Should().Be("https://payment.zarinpal.com/pg/v4/payment/request.json");
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("amount").ValueKind.Should().Be(JsonValueKind.Number);
        body.RootElement.GetProperty("amount").GetRawText().Should().Be("1500000");
        body.RootElement.GetProperty("currency").GetString().Should().Be("IRR");
        body.RootElement.GetProperty("merchant_id").GetString().Should().Be(MerchantId);
        body.RootElement.GetProperty("callback_url").GetString().Should().Be("https://shop.example/payment/callback");
    }

    [Fact]
    public async Task Verify_SendsTheSameIntegerRialAmount()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, """{"data":{"code":100,"ref_id":7},"errors":[]}""");

        await CreateGateway(handler).VerifyAsync("A0000000000000000000000000000000001", 1_500_000m);

        var request = handler.Requests.Should().ContainSingle().Which;
        request.Uri.Should().Be("https://payment.zarinpal.com/pg/v4/payment/verify.json");
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("amount").GetRawText().Should().Be("1500000");
        body.RootElement.GetProperty("authority").GetString().Should().Be("A0000000000000000000000000000000001");
    }

    [Fact]
    public async Task Initiate_FractionalAmount_IsRefused_WithoutCallingTheGateway()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, "{}");

        var result = await CreateGateway(handler).InitiateAsync(
            new PaymentInitiationRequest(42, 1851.75m, "https://shop.example/payment/callback"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_AMOUNT");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Initiate_GatewayErrorCode_IsFailure()
    {
        var handler = StubHandler.Json(HttpStatusCode.UnprocessableEntity, """{"data":[],"errors":{"code":-9,"message":"The input params invalid","validations":[]}}""");

        var result = await CreateGateway(handler).InitiateAsync(
            new PaymentInitiationRequest(42, 1_500_000m, "https://shop.example/payment/callback"));

        result.Success.Should().BeFalse();
        result.Authority.Should().BeNull();
    }

    // ==================== آدرس صفحهٔ پرداخت (F-28) ====================

    [Fact]
    public async Task RedirectUrl_Production_UsesTheConfiguredBaseUrl()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, """{"data":{"code":100,"authority":"A0000000000000000000000000000000001"},"errors":[]}""");
        var gateway = CreateGateway(handler);

        var result = await gateway.InitiateAsync(new PaymentInitiationRequest(42, 1_500_000m, "https://shop.example/cb"));

        result.RedirectUrl.Should().Be("https://payment.zarinpal.com/pg/StartPay/A0000000000000000000000000000000001");
        gateway.BuildRedirectUrl("A0000000000000000000000000000000001").Should().Be(result.RedirectUrl);
    }

    [Fact]
    public async Task RedirectUrl_Sandbox_UsesTheSandboxHost_ForRequestAndStartPay()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, """{"data":{"code":100,"authority":"S0000000000000000000000000000000001"},"errors":[]}""");
        var gateway = CreateGateway(handler, s => s.UseSandbox = true);

        var result = await gateway.InitiateAsync(new PaymentInitiationRequest(42, 1_500_000m, "https://shop.example/cb"));

        handler.Requests.Should().ContainSingle().Which.Uri.Should().Be("https://sandbox.zarinpal.com/pg/v4/payment/request.json");
        result.RedirectUrl.Should().Be("https://sandbox.zarinpal.com/pg/StartPay/S0000000000000000000000000000000001");
        gateway.BuildRedirectUrl("S0000000000000000000000000000000001").Should().Be(result.RedirectUrl);
    }

    // ==================== helpers ====================

    private static ZarinpalPaymentGateway CreateGateway(StubHandler handler, Action<PaymentGatewaySettings>? configure = null)
    {
        var settings = new PaymentGatewaySettings
        {
            Provider = "Zarinpal",
            MerchantId = MerchantId,
            EnableRealGateway = true,
            BaseUrl = "https://payment.zarinpal.com"
        };
        configure?.Invoke(settings);

        return new ZarinpalPaymentGateway(
            new HttpClient(handler),
            Options.Create(settings),
            NullLogger<ZarinpalPaymentGateway>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        private StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        public List<(string Uri, string Body)> Requests { get; } = new();

        public static StubHandler Json(HttpStatusCode status, string json)
            => new(() => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

        public static StubHandler Text(HttpStatusCode status, string text)
            => new(() => new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "text/html") });

        public static StubHandler Throws(Exception exception)
            => new(() => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), body));
            return _respond();
        }
    }
}
