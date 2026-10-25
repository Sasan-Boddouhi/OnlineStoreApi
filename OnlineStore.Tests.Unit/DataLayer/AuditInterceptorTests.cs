using Application.Entities;
using Application.Interfaces;
using DataLayer.Context;
using DataLayer.Repositories.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.DataLayer;

public class AuditInterceptorTests
{
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly AuditInterceptor _interceptor;
    private readonly AppDbContext _context;

    public AuditInterceptorTests()
    {
        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(c => c.TryGetCurrentUserId()).Returns(42);

        _interceptor = new AuditInterceptor(_currentUserMock.Object);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new AppDbContext(options, new[] { _interceptor });
    }

    [Fact]
    public void SavingChanges_OnAddedEntity_SetsAuditFields()
    {
        var product = new Product { Name = "Test", Price = 100, SubcategoryId = 1 };
        _context.Product.Add(product);
        _context.SaveChanges();

        product.CreatedOn.Should().NotBe(default(DateTime));
        product.ModifiedOn.Should().NotBe(default(DateTime));
        product.CreatedById.Should().Be(42);
        product.ModifiedById.Should().Be(42);
    }

    [Fact]
    public void SavingChanges_OnModifiedEntity_UpdatesModificationFields()
    {
        var product = new Product { Name = "Original", Price = 50, SubcategoryId = 1 };
        _context.Product.Add(product);
        _context.SaveChanges();

        var previousModifiedOn = product.ModifiedOn;

        product.Name = "Updated";
        _context.SaveChanges();

        product.ModifiedOn.Should().BeAfter(previousModifiedOn);
        product.ModifiedById.Should().Be(42);
        product.CreatedById.Should().Be(42); // unchanged
    }

    [Fact]
    public void SavingChanges_WithoutCurrentUser_DoesNotThrow()
    {
        var interceptor = new AuditInterceptor(new Mock<ICurrentUserService>().Object);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new AppDbContext(options, new[] { interceptor });

        var product = new Product { Name = "NoUser", Price = 10, SubcategoryId = 1 };
        context.Product.Add(product);
        context.SaveChanges();

        product.CreatedById.Should().BeNull();
    }
}