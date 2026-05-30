using Application.Common.Helpers;
using Application.Common.Queries;
using Application.Entities;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Queries;

public class FilterParserTests
{
    private static QueryParseContext<Product> CreateContext(HashSet<string>? allowed = null)
        => new()
        {
            AllowedFields = allowed ?? new HashSet<string> { "Name", "Price", "CategoryId" },
            CaseInsensitive = true
        };

    [Fact]
    public void TryParse_ValidEq_ReturnsOk()
    {
        var parser = new FilterParser();
        var result = parser.TryParse<Product>("Name eq 'test'", CreateContext());
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void TryParse_ValidGt_ReturnsOk()
    {
        var parser = new FilterParser();
        var result = parser.TryParse<Product>("Price gt 100", CreateContext());
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void TryParse_FieldNotAllowed_ReturnsError()
    {
        var parser = new FilterParser();
        var context = CreateContext(new HashSet<string> { "Name" });
        var result = parser.TryParse<Product>("Price eq 100", context);
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not allowed"));
    }

    [Fact]
    public void TryParse_UnclosedQuote_ReturnsError()
    {
        var parser = new FilterParser();
        var result = parser.TryParse<Product>("Name eq 'test", CreateContext());
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_UnsupportedOperator_ReturnsError()
    {
        var parser = new FilterParser();
        var result = parser.TryParse<Product>("Name xyz 'test'", CreateContext());
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_StringWithContains_ReturnsOk()
    {
        var parser = new FilterParser();
        var result = parser.TryParse<Product>("Name contains 'test'", CreateContext());
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void TryParse_NullValue_ReturnsOk()
    {
        var parser = new FilterParser();
        var result = parser.TryParse<Product>("Name eq null", CreateContext());
        result.Success.Should().BeTrue();
    }
}