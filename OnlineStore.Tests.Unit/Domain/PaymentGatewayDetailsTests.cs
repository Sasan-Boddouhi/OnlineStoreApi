using Application.Entities;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Domain;

/// <summary>
/// Batch 43 (FIN-07) — جزئیات درگاه روی پرداخت. قاعدهٔ سخت: شمارهٔ کامل کارت هرگز ذخیره نمی‌شود؛ فقط ۶ رقم اول و
/// ۴ رقم آخر، هر چه درگاه فرستاده باشد.
/// </summary>
public class PaymentGatewayDetailsTests
{
    [Theory]
    [InlineData("502229******5995", "502229******5995")]   // همان شکل مستند زرین‌پال
    [InlineData("6037991234561234", "603799******1234")]   // شمارهٔ کامل ⇒ ماسک می‌شود
    [InlineData("6037 9912 3456 1234", "603799******1234")] // با فاصله
    [InlineData("6037-9912-3456-1234", "603799******1234")] // با خط تیره
    [InlineData("60379912****1234", "603799******1234")]   // ماسکِ ضعیف‌تر (۸ رقم اول) ⇒ سخت‌تر می‌شود
    [InlineData("603799XXXXXX1234", "603799******1234")]   // نویسهٔ ماسک دیگر
    [InlineData("5022291234567895995", "502229*********5995")] // کارت ۱۹ رقمی
    public void MaskCardPan_KeepsOnlyTheFirstSixAndLastFourDigits(string input, string expected)
    {
        Payment.MaskCardPan(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]                    // کوتاه‌تر از یک شمارهٔ کارت
    [InlineData("60379912345612341234567")] // بلندتر از ۱۹
    [InlineData("card: 6037991234561234")]  // نویسهٔ غیرمنتظره
    [InlineData("6037991234561234<script>")]
    public void MaskCardPan_InvalidInput_StoresNothing(string? input)
    {
        Payment.MaskCardPan(input).Should().BeNull();
    }

    [Fact]
    public void RecordGatewayDetails_StoresFeeFeeTypeAndTheMaskedCard()
    {
        var payment = new Payment();

        payment.RecordGatewayDetails(12_000m, " Merchant ", "6037991234561234");

        payment.GatewayFee.Should().Be(12_000m);
        payment.GatewayFeeType.Should().Be("Merchant");
        payment.CardPanMasked.Should().Be("603799******1234");
    }

    [Fact]
    public void RecordGatewayDetails_EmptyValues_DoNotEraseWhatIsAlreadyStored()
    {
        var payment = new Payment();
        payment.RecordGatewayDetails(12_000m, "Merchant", "502229******5995");

        // تأیید تکراری (101) ممکن است جزئیات کمتری برگرداند.
        payment.RecordGatewayDetails(null, null, null);
        payment.RecordGatewayDetails(-1m, "  ", "not-a-card");

        payment.GatewayFee.Should().Be(12_000m);
        payment.GatewayFeeType.Should().Be("Merchant");
        payment.CardPanMasked.Should().Be("502229******5995");
    }

    [Fact]
    public void RecordGatewayDetails_TruncatesTheFeeTypeToTheColumnLength_AndTheFeeToWholeRial()
    {
        var payment = new Payment();

        payment.RecordGatewayDetails(1_234.9m, new string('M', 40), null);

        payment.GatewayFee.Should().Be(1_234m);
        payment.GatewayFeeType.Should().HaveLength(20);
        payment.CardPanMasked.Should().BeNull();
    }
}
