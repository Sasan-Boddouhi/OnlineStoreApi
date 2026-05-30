using BusinessLogic.Common.Validation;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Validators;

public class PersianDateValidatorTests
{
    [Theory]
    [InlineData("1402/01/01", true)]
    [InlineData("1402/06/31", true)]   // شهریور ۳۱ روزه
    [InlineData("1402/12/29", true)]   // اسفند ۲۹ روزه (سال غیر کبیسه)
    [InlineData("1403/12/30", true)]   // سال کبیسه، اسفند ۳۰ روزه
    [InlineData("1402/12/30", false)]  // غیر کبیسه، اسفند ۳۰ ندارد
    [InlineData("1402/13/01", false)]  // ماه نامعتبر
    [InlineData("1300/01/01", true)]   // سال ۱۳۰۰ معتبر است (کد year < 1300 را رد می‌کند)
    [InlineData("1299/01/01", false)]  // سال کمتر از ۱۳۰۰ نامعتبر
    [InlineData("1501/01/01", false)]  // سال بیشتر از ۱۵۰۰
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ReturnsExpected(string? input, bool expected)
    {
        PersianDateValidator.IsValid(input).Should().Be(expected);
    }
}