using Application.Options;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Online_Store_Application.Extensions;

namespace OnlineStore.Tests.Unit.Configuration;

/// <summary>Batch 48c: ProductImages:AllowedExternalHosts فقط نام میزبان می‌پذیرد.</summary>
public class ProductImageUrlOptionsTests
{
    private static ValidateOptionsResult Validate(params string[] hosts)
        => new ProductImageUrlOptionsValidator().Validate(null, new ProductImageUrlOptions { AllowedExternalHosts = hosts });

    [Fact]
    public void Default_IsEmptyList_AndValid()
    {
        new ProductImageUrlOptions().AllowedExternalHosts.Should().BeEmpty();
        Validate().Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("cdn.example.com")]
    [InlineData("CDN.Example.com")]
    [InlineData("localhost")]
    [InlineData("a-b.example.co.uk")]
    public void ValidHost_IsAccepted(string host) => Validate(host).Succeeded.Should().BeTrue();

    [Theory]
    [InlineData("https://x.com")]
    [InlineData("x.com/path")]
    [InlineData("x.com:8080")]
    [InlineData("*.x.com")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" x.com")]
    [InlineData("x.com ")]
    [InlineData("a@x.com")]
    public void InvalidHost_FailsWithPersianMessage(string host)
    {
        var result = Validate("ok.example.com", host);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AllowedExternalHosts:1").And.Contain("نامعتبر");
    }
}
