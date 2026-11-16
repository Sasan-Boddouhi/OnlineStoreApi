using Application.Common.Helpers;
using Application.Exceptions;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Helpers;

/// <summary>Batch 48b: every accepted spelling of an Iranian mobile number becomes the one canonical 09xxxxxxxxx.</summary>
public class IranianPhoneNumberTests
{
    private const string Canonical = "09123456789";

    [Theory]
    [InlineData("09123456789")]
    [InlineData("+989123456789")]
    [InlineData("00989123456789")]
    [InlineData("989123456789")]
    [InlineData("9123456789")]
    [InlineData("0912 345 6789")]
    [InlineData("0912-345-6789")]
    [InlineData("+98 912 345 6789")]
    [InlineData("۰۹۱۲۳۴۵۶۷۸۹")]                 // Persian digits
    [InlineData("٠٩١٢٣٤٥٦٧٨٩")]                 // Arabic-Indic digits
    [InlineData("+۹۸۹۱۲۳۴۵۶۷۸۹")]               // + with Persian digits
    [InlineData("  (0912) 345.6789  ")]
    public void TryNormalize_AcceptedVariant_ReturnsCanonical(string input)
    {
        IranianPhoneNumber.TryNormalize(input, out var canonical).Should().BeTrue();
        canonical.Should().Be(Canonical);
        IranianPhoneNumber.NormalizeOrNull(input).Should().Be(Canonical);
        IranianPhoneNumber.NormalizeOrThrow(input).Should().Be(Canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("02112345678")]       // landline
    [InlineData("0912345678")]        // 10 digits with a leading 0
    [InlineData("091234567890")]      // 12 digits
    [InlineData("09l23456789")]       // letter l instead of 1
    [InlineData("+1 912 345 6789")]   // another country code
    [InlineData("++989123456789")]    // a second +
    [InlineData("98+9123456789")]     // + not at the start
    [InlineData("+09123456789")]      // + without 98
    [InlineData("982112345678")]      // 98 + landline
    public void TryNormalize_OtherInput_IsNotNormalisable(string? input)
    {
        IranianPhoneNumber.TryNormalize(input, out var canonical).Should().BeFalse();
        canonical.Should().BeEmpty();
        IranianPhoneNumber.NormalizeOrNull(input).Should().BeNull();

        var act = () => IranianPhoneNumber.NormalizeOrThrow(input);
        act.Should().Throw<UnprocessableEntityException>()
            .Which.Code.Should().Be(IranianPhoneNumber.InvalidErrorCode);
    }

    [Fact]
    public void IsValid_MatchesTryNormalize()
    {
        IranianPhoneNumber.IsValid("+98 912 345 6789").Should().BeTrue();
        IranianPhoneNumber.IsValid("02112345678").Should().BeFalse();
    }
}
