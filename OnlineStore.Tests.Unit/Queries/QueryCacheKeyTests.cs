using System.Linq.Expressions;
using Application.Common.Queries;
using Application.Entities;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Queries;

/// <summary>
/// F-23: کلید کش query باید طول ثابت داشته باشد، پایدار باشد،
/// و برای queryهای متفاوت (فیلتر یا مرتب‌سازی) متفاوت باشد.
/// </summary>
public class QueryCacheKeyTests
{
    private const string KeyPattern = "^products:q:[0-9a-f]{64}$";

    private static QueryParseContext<Product> CreateContext() => new()
    {
        AllowedFields = new HashSet<string> { "Name", "Price" },
        CaseInsensitive = true
    };

    private static QueryContract<Product> Parse(string? filter, string? sort)
    {
        var result = StringQueryParser.TryParse<Product>(filter, sort, CreateContext(), page: 1, size: 20);
        result.Success.Should().BeTrue();
        return result.Value!;
    }

    [Fact]
    public void ToCacheKey_HugeFilter_HasBoundedLength()
    {
        // Batch 60: فیلتر بیش از FilterParser.MaxLength نویسه رد می‌شود؛ بزرگ‌ترین فیلتر مجاز را می‌سازیم.
        var query = Parse($"Name contains '{new string('x', FilterParser.MaxLength - "Name contains ''".Length)}'", null);

        var key = query.ToCacheKey("products");

        key.Should().MatchRegex(KeyPattern);
        key.Length.Should().Be("products:q:".Length + 64);
    }

    [Fact]
    public void ToCacheKey_SameQueryParsedTwice_IsStable()
    {
        var key1 = Parse("Name contains 'phone'", "-Price").ToCacheKey("products");
        var key2 = Parse("Name contains 'phone'", "-Price").ToCacheKey("products");

        key1.Should().Be(key2);
    }

    [Fact]
    public void ToCacheKey_DifferentFilterValues_DiffersKey()
    {
        var key1 = Parse("Name contains 'phone'", null).ToCacheKey("products");
        var key2 = Parse("Name contains 'laptop'", null).ToCacheKey("products");

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void ToCacheKey_DifferentSortFields_DiffersKey()
    {
        // پیش از F-23 مرتب‌سازی فقط با نام نوعش در کلید می‌آمد و این دو کلید یکسان بودند.
        var byName = Parse(null, "Name").ToCacheKey("products");
        var byPrice = Parse(null, "Price").ToCacheKey("products");

        byName.Should().NotBe(byPrice);
    }

    [Fact]
    public void ToCacheKey_DifferentSortDirections_DiffersKey()
    {
        var asc = Parse(null, "Price").ToCacheKey("products");
        var desc = Parse(null, "-Price").ToCacheKey("products");

        asc.Should().NotBe(desc);
    }

    [Fact]
    public void ToCacheKey_StringConstantsWithQuotes_DoNotCollide()
    {
        // Expression.ToString کوتیشن داخل ثابت را escape نمی‌کند؛ این دو فیلتر متن یکسان دارند ولی معنای متفاوت.
        Expression<Func<Product, bool>> first = p => p.Name.Replace("a\", \"b", "c") == "z";
        Expression<Func<Product, bool>> second = p => p.Name.Replace("a", "b\", \"c") == "z";
        first.ToString().Should().Be(second.ToString(), "precondition: the plain text of both filters collides");

        var key1 = new QueryContract<Product> { Filter = first }.ToCacheKey("products");
        var key2 = new QueryContract<Product> { Filter = second }.ToCacheKey("products");

        key1.Should().NotBe(key2);
    }
}
