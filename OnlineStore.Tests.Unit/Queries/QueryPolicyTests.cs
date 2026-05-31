using Application.Common.Queries;
using Application.Entities;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Queries;

public class QueryPolicyTests
{
    private static QueryParseContext<Product> CreateContext(int maxPageSize = 100)
        => new() { MaxPageSize = maxPageSize };

    // ----- Validate -----
    [Fact]
    public void Validate_ValidContract_ReturnsOk()
    {
        var query = new QueryContract<Product> { Page = 1, Size = 10 };
        var result = QueryPolicy.Validate(query, CreateContext());
        result.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PageLessThanOne_ReturnsError(int page)
    {
        var query = new QueryContract<Product> { Page = page };
        var result = QueryPolicy.Validate(query, CreateContext());
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "invalid_page");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_SizeInvalid_ReturnsError(int size)
    {
        var query = new QueryContract<Product> { Size = size };
        var result = QueryPolicy.Validate(query, CreateContext(100));
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "invalid_size");
    }

    [Fact]
    public void Validate_SkipNegative_ReturnsError()
    {
        var query = new QueryContract<Product> { Skip = -1 };
        var result = QueryPolicy.Validate(query, CreateContext());
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "invalid_skip");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_TakeInvalid_ReturnsError(int take)
    {
        var query = new QueryContract<Product> { Take = take };
        var result = QueryPolicy.Validate(query, CreateContext(100));
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "invalid_take");
    }

    // ----- Normalize -----
    [Fact]
    public void Normalize_WithSkipTake_ClampsValues()
    {
        var query = new QueryContract<Product> { Skip = -5, Take = 200 };
        var normalized = QueryPolicy.Normalize(query, CreateContext(50));
        normalized.Skip.Should().Be(0);
        normalized.Take.Should().Be(50);
    }

    [Fact]
    public void Normalize_WithPageSize_ClampsValues()
    {
        var query = new QueryContract<Product> { Page = 0, Size = 200 };
        var normalized = QueryPolicy.Normalize(query, CreateContext(30));
        normalized.Page.Should().Be(1);
        normalized.Size.Should().Be(30);
    }
}