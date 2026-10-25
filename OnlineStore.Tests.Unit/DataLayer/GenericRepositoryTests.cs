using Application.Common.Specifications;
using Application.Entities;
using DataLayer.Context;
using DataLayer.Repositories.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OnlineStore.Tests.Unit.DataLayer;

public class GenericRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly GenericRepository<Product> _repository;

    public GenericRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new AppDbContext(options, Enumerable.Empty<ISaveChangesInterceptor>());
        _repository = new GenericRepository<Product>(_context);
    }

    [Fact]
    public async Task AddAsync_ShouldAddEntity()
    {
        var product = new Product
        {
            Name = "Test",
            Price = 10,
            SubcategoryId = 1
        };

        await _repository.AddAsync(product);
        await _context.SaveChangesAsync();

        var result = await _context.Product.FindAsync(product.ProductId);
        Assert.NotNull(result);
        Assert.Equal("Test", result!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnEntity_WhenExists()
    {
        var product = new Product
        {
            Name = "Find",
            Price = 20,
            SubcategoryId = 1
        };

        _context.Product.Add(product);
        await _context.SaveChangesAsync();

        var found = await _repository.GetByIdAsync(product.ProductId);
        Assert.NotNull(found);
        Assert.Equal("Find", found!.Name);
    }

    [Fact]
    public async Task AnyAsync_WithPredicate_ReturnsTrue()
    {
        _context.Product.Add(new Product
        {
            Name = "P1",
            Price = 5,
            SubcategoryId = 1
        });
        await _context.SaveChangesAsync();

        var exists = await _repository.AnyAsync(p => p.Name == "P1");
        Assert.True(exists);
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectNumber()
    {
        _context.Product.Add(new Product { Name = "A", Price = 1, SubcategoryId = 1 });
        _context.Product.Add(new Product { Name = "B", Price = 2, SubcategoryId = 1 });
        await _context.SaveChangesAsync();

        var count = await _repository.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Delete_ShouldRemoveEntity()
    {
        var product = new Product
        {
            Name = "ToDelete",
            Price = 1,
            SubcategoryId = 1
        };

        _context.Product.Add(product);
        await _context.SaveChangesAsync();

        _repository.Delete(product);
        await _context.SaveChangesAsync();

        var deleted = await _context.Product.FindAsync(product.ProductId);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task Update_ShouldModifyEntity()
    {
        var product = new Product
        {
            Name = "Old",
            Price = 10,
            SubcategoryId = 1
        };

        _context.Product.Add(product);
        await _context.SaveChangesAsync();

        product.Name = "New";
        _repository.Update(product);
        await _context.SaveChangesAsync();

        var updated = await _context.Product.FindAsync(product.ProductId);
        Assert.Equal("New", updated!.Name);
    }

    [Fact]
    public async Task AddRangeAsync_ShouldAddMultipleEntities()
    {
        var products = new List<Product>
        {
            new() { Name = "P1", Price = 1, SubcategoryId = 1 },
            new() { Name = "P2", Price = 2, SubcategoryId = 1 }
        };

        await _repository.AddRangeAsync(products);
        await _context.SaveChangesAsync();

        var count = await _context.Product.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithSpec_ShouldReturnMatchingEntity()
    {
        _context.Product.Add(new Product { Name = "Target", Price = 100, SubcategoryId = 1 });
        await _context.SaveChangesAsync();

        var spec = new Spec<Product>().Where(p => p.Name == "Target");
        var result = await _repository.FirstOrDefaultAsync(spec);

        Assert.NotNull(result);
        Assert.Equal("Target", result!.Name);
    }

    [Fact]
    public async Task ListAsync_WithSpec_ShouldReturnMatchingEntities()
    {
        _context.Product.Add(new Product { Name = "A", Price = 1, SubcategoryId = 1 });
        _context.Product.Add(new Product { Name = "B", Price = 2, SubcategoryId = 1 });
        _context.Product.Add(new Product { Name = "C", Price = 3, SubcategoryId = 1 });
        await _context.SaveChangesAsync();

        var spec = new Spec<Product>().Where(p => p.Price >= 2);
        var results = await _repository.ListAsync(spec);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task CountAsync_WithSpec_ShouldReturnCorrectCount()
    {
        _context.Product.Add(new Product { Name = "A", Price = 1, SubcategoryId = 1 });
        _context.Product.Add(new Product { Name = "B", Price = 2, SubcategoryId = 1 });
        await _context.SaveChangesAsync();

        var spec = new Spec<Product>().Where(p => p.Price > 1);
        var count = await _repository.CountAsync(spec);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AnyAsync_WithSpec_ShouldReturnTrueWhenMatch()
    {
        _context.Product.Add(new Product { Name = "Exists", Price = 10, SubcategoryId = 1 });
        await _context.SaveChangesAsync();

        var spec = new Spec<Product>().Where(p => p.Name == "Exists");
        var exists = await _repository.AnyAsync(spec);

        Assert.True(exists);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithProjection_ShouldReturnDto()
    {
        _context.Product.Add(new Product { Name = "Proj", Price = 50, SubcategoryId = 1 });
        await _context.SaveChangesAsync();

        var spec = new Spec<Product>().Where(p => p.Name == "Proj");
        var result = await _repository.FirstOrDefaultAsync(
            spec,
            p => new { p.Name, p.Price });

        Assert.NotNull(result);
        Assert.Equal("Proj", result!.Name);
        Assert.Equal(50, result.Price);
    }

    [Fact]
    public async Task ListAsync_WithProjection_ShouldReturnDtos()
    {
        _context.Product.Add(new Product { Name = "X", Price = 1, SubcategoryId = 1 });
        _context.Product.Add(new Product { Name = "Y", Price = 2, SubcategoryId = 1 });
        await _context.SaveChangesAsync();

        var spec = new Spec<Product>();
        var results = await _repository.ListAsync(spec, p => new { p.Name });

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Query_ShouldReturnIQueryable()
    {
        _context.Product.Add(new Product { Name = "Q", Price = 1, SubcategoryId = 1 });
        _context.SaveChanges();

        var query = _repository.Query();
        var result = query.FirstOrDefault(p => p.Name == "Q");

        Assert.NotNull(result);
    }

    [Fact]
    public void UpdateRange_ShouldMarkMultipleAsModified()
    {
        var products = new List<Product>
        {
            new() { Name = "U1", Price = 1, SubcategoryId = 1 },
            new() { Name = "U2", Price = 2, SubcategoryId = 1 }
        };

        _context.Product.AddRange(products);
        _context.SaveChanges();

        products[0].Name = "Updated1";
        products[1].Name = "Updated2";
        _repository.UpdateRange(products);
        _context.SaveChanges();

        var updated = _context.Product.Where(p => p.Name.StartsWith("Updated")).ToList();
        Assert.Equal(2, updated.Count);
    }

    [Fact]
    public void DeleteRange_ShouldRemoveMultipleEntities()
    {
        var products = new List<Product>
        {
            new() { Name = "D1", Price = 1, SubcategoryId = 1 },
            new() { Name = "D2", Price = 2, SubcategoryId = 1 }
        };

        _context.Product.AddRange(products);
        _context.SaveChanges();

        _repository.DeleteRange(products);
        _context.SaveChanges();

        var remaining = _context.Product.ToList();
        Assert.Empty(remaining);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}