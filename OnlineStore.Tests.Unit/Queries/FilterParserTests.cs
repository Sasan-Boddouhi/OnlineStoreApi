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
        // Batch 60: پیام‌ها فارسی شدند؛ نام فیلد و کد خطا همچنان ثابت‌اند.
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Price") && e.Code == "filter_parse_error");
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

    // ==================== Batch 60 (F-24) ====================

    private static readonly Product[] Sample =
    {
        P(1, "apple", 5m),
        P(2, "apple", 50m),
        P(3, "banana", 5m),
        P(4, "banana", 50m),
        P(5, "cherry", 500m),
    };

    private static Product P(int id, string name, decimal price)
        => new() { ProductId = id, Name = name, Price = price, SubcategoryId = 1 };

    private static int[] Apply(string filter)
    {
        var result = new FilterParser().TryParse<Product>(filter, CreateContext());
        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        var predicate = result.Value!.Compile();
        return Sample.Where(predicate).Select(p => p.ProductId).OrderBy(i => i).ToArray();
    }

    private static ParseResult<System.Linq.Expressions.Expression<Func<Product, bool>>> Parse(string filter)
        => new FilterParser().TryParse<Product>(filter, CreateContext());

    [Fact]
    public void TryParse_TwoConditionsWithAnd_AppliesBoth()
    {
        // پیش از Batch 60 فقط شرط اول اعمال می‌شد و نتیجه {1, 2} بود.
        Apply("Name eq 'apple' and Price gt 10").Should().Equal(2);
    }

    [Fact]
    public void TryParse_Or_AppliesEitherCondition()
    {
        Apply("Name eq 'apple' or Name eq 'cherry'").Should().Equal(1, 2, 5);
    }

    [Fact]
    public void TryParse_AndBindsTighterThanOr()
    {
        // a or (b and c)
        Apply("Name eq 'cherry' or Name eq 'banana' and Price lt 10").Should().Equal(3, 5);
    }

    [Fact]
    public void TryParse_ParenthesesAndNot_AreApplied()
    {
        Apply("(Name eq 'apple' or Name eq 'banana') and not Price gt 10").Should().Equal(1, 3);
    }

    [Fact]
    public void TryParse_KeywordsAreCaseInsensitive()
    {
        Apply("Name EQ 'apple' AND Price GT 10").Should().Equal(2);
    }

    [Theory]
    [InlineData("Name eq 'apple' garbage")]
    [InlineData("Name eq 'apple' Price gt 10")]
    [InlineData("Name eq 'apple' and")]
    [InlineData("Name eq 'apple' or")]
    [InlineData("Name eq 'apple' )")]
    [InlineData("Name eq 'apple', Price gt 1")]
    public void TryParse_TrailingOrDanglingTokens_Fail(string filter)
    {
        // پیش از Batch 60 هر چیزی پس از اولین مقایسه بی‌صدا نادیده گرفته می‌شد.
        var result = Parse(filter);
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be("filter_parse_error");
    }

    [Fact]
    public void TryParse_UnclosedParenthesis_Fails()
    {
        Parse("(Name eq 'apple'").Success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_MissingValue_Fails()
    {
        // پیش از Batch 60 توکن پایان (Eof، مقدار خالی) به‌عنوان مقدار رشته‌ای "" پذیرفته می‌شد.
        Parse("Name eq").Success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_UnquotedIdentifierValue_Fails()
    {
        // مقدار رشته‌ای باید در کوتیشن باشد؛ وگرنه «Name eq and» یا «Name eq x and ...» مبهم می‌شود.
        Parse("Name eq apple").Success.Should().BeFalse();
        Parse("Name eq and").Success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_TooManyConditions_Fails()
    {
        // سقف: ۲۰ مقایسه
        Parse(string.Join(" or ", Enumerable.Repeat("Name eq 'a'", 21))).Success.Should().BeFalse();
        Parse(string.Join(" or ", Enumerable.Repeat("Name eq 'a'", 20))).Success.Should().BeTrue();
    }

    [Fact]
    public void TryParse_TooDeepNesting_Fails()
    {
        // سقف: عمق ۸ (پرانتز و not)
        Parse(new string('(', 9) + "Name eq 'a'" + new string(')', 9)).Success.Should().BeFalse();
        Parse(new string('(', 8) + "Name eq 'a'" + new string(')', 8)).Success.Should().BeTrue();
        Parse(string.Concat(Enumerable.Repeat("not ", 9)) + "Name eq 'a'").Success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_TooLong_Fails()
    {
        // سقف: ۱۰۰۰ نویسه
        Parse($"Name eq '{new string('x', 1000)}'").Success.Should().BeFalse();
    }

    [Fact]
    public void TryParse_UnknownPropertyWithoutWhitelist_FailsWithoutLeakingInternals()
    {
        var context = new QueryParseContext<Product> { AllowedFields = new HashSet<string>(), CaseInsensitive = true };
        var result = new FilterParser().TryParse<Product>("NoSuchField eq 'x'", context);

        result.Success.Should().BeFalse();
        result.Errors.Single().Message.Should().NotContain("Instance property");
    }

    [Fact]
    public void TryParse_FieldInDifferentCase_ResolvesToSameProperty()
    {
        Apply("nAmE eq 'apple' and PRICE gt 10").Should().Equal(2);
    }

    // ==================== Expression cache (F-24) ====================

    private sealed class CaseProbe
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void ExpressionCache_FieldSpelledInDifferentCase_SharesOneEntry()
    {
        // پیش از Batch 60 کلید کش متن خام فیلد بود؛ هر ترکیب حروف بزرگ و کوچک یک ورودی تازه می‌ساخت.
        var first = ExpressionBuilder.BuildPropertyLambdaCached<CaseProbe>("name");
        var second = ExpressionBuilder.BuildPropertyLambdaCached<CaseProbe>("NAME");
        var third = ExpressionBuilder.BuildPropertyLambdaCached<CaseProbe>("nAmE");

        second.Should().BeSameAs(first);
        third.Should().BeSameAs(first);
    }

    private sealed class CacheProbe
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void ExpressionCache_IsBounded()
    {
        // پیش از Batch 60 کش هیچ سقفی نداشت (این آزمون روی کد قدیم کامپایل نمی‌شد: Count و MaxEntries نبودند).
        var lambda = ExpressionBuilder.BuildPropertyLambdaCached<CaseProbe>("Name");
        for (var i = 0; i < ExpressionCache<CacheProbe>.MaxEntries + 50; i++)
            ExpressionCache<CacheProbe>.GetOrAdd($"key-{i}", _ => lambda).Should().BeSameAs(lambda);

        ExpressionCache<CacheProbe>.Count.Should().Be(ExpressionCache<CacheProbe>.MaxEntries);
    }

    [Fact]
    public void TryParse_ManyUnknownFieldsWithoutWhitelist_DoNotGrowTheCache()
    {
        var context = new QueryParseContext<Product> { AllowedFields = new HashSet<string>(), CaseInsensitive = true };
        var before = ExpressionCache<Product>.Count;

        for (var i = 0; i < 500; i++)
            new FilterParser().TryParse<Product>($"field{i} eq 'x'", context).Success.Should().BeFalse();

        ExpressionCache<Product>.Count.Should().BeLessThanOrEqualTo(Math.Max(before, ExpressionCache<Product>.MaxEntries));
    }

    [Fact]
    public void FilterParser_LimitsMatchTheDocumentedGrammar()
    {
        FilterParser.MaxLength.Should().Be(1000);
        FilterParser.MaxComparisons.Should().Be(20);
        FilterParser.MaxDepth.Should().Be(8);
    }
}
