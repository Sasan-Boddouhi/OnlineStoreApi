using Application.Entities;
using DataLayer.Context;
using DataLayer.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OnlineStore.Tests.Unit.DataLayer;

public class UnitOfWorkTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly UnitOfWork _unitOfWork;

    public UnitOfWorkTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new AppDbContext(options, Enumerable.Empty<ISaveChangesInterceptor>());
        _unitOfWork = new UnitOfWork(_context);
    }

    [Fact]
    public void Repository_ShouldReturnSameInstanceForSameType()
    {
        var repo1 = _unitOfWork.Repository<Product>();
        var repo2 = _unitOfWork.Repository<Product>();
        Assert.Same(repo1, repo2);
    }

    [Fact]
    public void Repository_ShouldReturnDifferentInstancesForDifferentTypes()
    {
        var productRepo = _unitOfWork.Repository<Product>();
        var categoryRepo = _unitOfWork.Repository<ProductCategory>();
        Assert.NotSame(productRepo, categoryRepo);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldPersistChanges()
    {
        var product = new Product { Name = "Saved", Price = 10, SubcategoryId = 1 };
        _context.Product.Add(product);
        var result = await _unitOfWork.SaveChangesAsync();
        Assert.Equal(1, result);
        Assert.True(product.ProductId > 0);
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        await _unitOfWork.DisposeAsync();
        Assert.True(true);
    }

    public void Dispose()
    {
        _unitOfWork.Dispose();
        _context.Dispose();
    }
}