using Application.Options;
using FluentAssertions;
using Online_Store_Application.Extensions;

namespace OnlineStore.Tests.Unit.Configuration;

/// <summary>Batch 49b — اعتبارسنجی <see cref="PlatformWarehouseOptions"/> و آداپتور ValidateOnStart آن.</summary>
public class PlatformWarehouseOptionsTests
{
    private static PlatformWarehouseOptions Valid() => new()
    {
        Name = "انبار مرکزی",
        ProvinceName = "تهران",
        CityName = "تهران",
        StreetAddress = "خیابان آزادی",
        Plaque = "10",
        Unit = "2",
        PostalCode = "1234567890",
        RecipientFirstName = "مسئول",
        RecipientLastName = "انبار",
        PhoneNumber = "09121234567"
    };

    [Fact]
    public void Defaults_AreNotSpecified_AndNotValid()
    {
        var options = new PlatformWarehouseOptions();

        options.IsSpecified.Should().BeFalse();
        options.Name.Should().NotBeNullOrWhiteSpace();
        options.Validate().Should().Contain(e => e.Contains("PlatformWarehouse:ProvinceName"))
            .And.Contain(e => e.Contains("PlatformWarehouse:PhoneNumber"))
            .And.Contain(e => e.Contains("PlatformWarehouse:PostalCode"));
    }

    [Fact]
    public void FullConfiguration_IsValid()
    {
        var options = Valid();

        options.IsSpecified.Should().BeTrue();
        options.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Unit_IsOptional()
    {
        var options = Valid();
        options.Unit = "";

        options.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData("+98 912 123 4567")]
    [InlineData("۰۹۱۲۱۲۳۴۵۶۷")]
    [InlineData("9121234567")]
    [InlineData("0098-912-123-4567")]
    public void PhoneNumber_IsNormalised(string phone)
    {
        var options = Valid();
        options.PhoneNumber = phone;

        options.Validate().Should().BeEmpty();
        options.NormalizedPhoneNumber.Should().Be("09121234567");
    }

    [Theory]
    [InlineData("02112345678")]
    [InlineData("12345")]
    [InlineData("")]
    public void InvalidPhoneNumber_IsRejected(string phone)
    {
        var options = Valid();
        options.PhoneNumber = phone;

        options.Validate().Should().ContainSingle(e => e.Contains("PlatformWarehouse:PhoneNumber"));
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("12345-6789")]
    [InlineData("۱۲۳۴۵۶۷۸۹۰")]
    public void InvalidPostalCode_IsRejected(string postalCode)
    {
        var options = Valid();
        options.PostalCode = postalCode;

        options.Validate().Should().ContainSingle(e => e.Contains("PlatformWarehouse:PostalCode"));
    }

    [Fact]
    public void TooLongFields_AreRejected()
    {
        var options = Valid();
        options.Name = new string('ن', 151);
        options.Plaque = new string('1', 11);
        options.Unit = new string('1', 21);

        options.Validate().Should().HaveCount(3)
            .And.Contain(e => e.Contains("PlatformWarehouse:Name"))
            .And.Contain(e => e.Contains("PlatformWarehouse:Plaque"))
            .And.Contain(e => e.Contains("PlatformWarehouse:Unit"));
    }

    [Fact]
    public void Validator_NotSpecified_Succeeds()
    {
        new PlatformWarehouseOptionsValidator().Validate(null, new PlatformWarehouseOptions())
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validator_PartiallySpecified_Fails()
    {
        var options = new PlatformWarehouseOptions { ProvinceName = "تهران" };

        var result = new PlatformWarehouseOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PlatformWarehouse:CityName");
    }

    [Fact]
    public void Validator_FullySpecified_Succeeds()
    {
        new PlatformWarehouseOptionsValidator().Validate(null, Valid())
            .Succeeded.Should().BeTrue();
    }
}
