using Application.Common.Queries;
using Application.Entities;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Queries;

public class SortParserTests
{
    // یک متد کمکی برای ساخت Product با پر کردن تمام فیلدهای required
    private static Product CreateProduct(int id, string name, decimal price) =>
        new()
        {
            ProductId = id,
            Name = name,
            Price = price,
            SubcategoryId = 1
        };

    [Fact]
    public void TryParse_AscendingByName()
    {
        var context = new QueryParseContext<Product>
        {
            AllowedFields = new HashSet<string> { "Name", "Price" },
            CaseInsensitive = true
        };
        var result = SortParser.TryParse<Product>("Name asc", context);
        result.Success.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public void TryParse_DescendingWithMinus()
    {
        var context = new QueryParseContext<Product>
        {
            AllowedFields = new HashSet<string> { "Name", "Price" },
            CaseInsensitive = true
        };
        var result = SortParser.TryParse<Product>("-Price", context);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void TryParse_DescendingWithDesc()
    {
        var context = new QueryParseContext<Product>
        {
            AllowedFields = new HashSet<string> { "Name", "Price" },
            CaseInsensitive = true
        };
        var result = SortParser.TryParse<Product>("Name desc", context);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void TryParse_MultipleFields()
    {
        var context = new QueryParseContext<Product>
        {
            AllowedFields = new HashSet<string> { "Name", "Price" },
            CaseInsensitive = true
        };
        var result = SortParser.TryParse<Product>("Name asc, Price desc", context);
        result.Success.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public void TryParse_FieldNotAllowed_ReturnsError()
    {
        var context = new QueryParseContext<Product>
        {
            AllowedFields = new HashSet<string> { "Name" },
            CaseInsensitive = true
        };
        var result = SortParser.TryParse<Product>("Price asc", context);
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not allowed"));
    }

    [Fact]
    public void TryParse_UnknownProperty_ReturnsError()
    {
        var context = new QueryParseContext<Product>
        {
            AllowedFields = new HashSet<string> { "NonExistent" },
            CaseInsensitive = true
        };
        var result = SortParser.TryParse<Product>("NonExistent asc", context);
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    [Fact]
    public void TryParse_EmptySort_ReturnsOkWithEmptyList()
    {
        var context = new QueryParseContext<Product> { AllowedFields = null! };
        var result = SortParser.TryParse<Product>(null, context);
        result.Success.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}