using Application.Common.Specifications;
using Application.Entities;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Specifications;

public class SpecificationEvaluatorTests
{
    private readonly List<Product> _data;

    public SpecificationEvaluatorTests()
    {
        _data = new()
        {
            new Product { ProductId = 1, Name = "A", Price = 100, SubcategoryId = 1 },
            new Product { ProductId = 2, Name = "B", Price = 200, SubcategoryId = 1 },
            new Product { ProductId = 3, Name = "C", Price = 150, SubcategoryId = 1 }
        };
    }

    private IQueryable<Product> GetQueryable() => _data.AsQueryable();

    [Fact]
    public void GetQuery_WithCriteria_Filters()
    {
        var spec = new Spec<Product>().Where(p => p.Price > 100);
        var result = SpecificationEvaluator<Product>.GetQuery(GetQueryable(), spec).ToList();
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "B" || p.Name == "C");
    }

    [Fact]
    public void GetQuery_WithOrdering_Ascending()
    {
        var spec = new Spec<Product>().OrderBy(p => p.Name);
        var result = SpecificationEvaluator<Product>.GetQuery(GetQueryable(), spec).ToList();
        result[0].Name.Should().Be("A");
        result[1].Name.Should().Be("B");
        result[2].Name.Should().Be("C");
    }

    [Fact]
    public void GetQuery_WithOrdering_Descending()
    {
        var spec = new Spec<Product>().OrderByDescending(p => p.Price);
        var result = SpecificationEvaluator<Product>.GetQuery(GetQueryable(), spec).ToList();
        result[0].Price.Should().Be(200);
        result[2].Price.Should().Be(100);
    }

    [Fact]
    public void GetQuery_WithInclude_IncludesNavigation()
    {
        var spec = new Spec<Product>().Include(p => p.Subcategory);
        var act = () => SpecificationEvaluator<Product>.GetQuery(GetQueryable(), spec).ToList();
        act.Should().NotThrow();
    }
}