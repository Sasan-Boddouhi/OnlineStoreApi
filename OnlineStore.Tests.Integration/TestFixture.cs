using Application.Entities;
using Application.Interfaces;
using Application.Interfaces.Security;
using AutoMapper;
using BusinessLogic.Extensions;
using DataLayer.Context;
using DataLayer.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OnlineStore.Tests.Integration;

public class TestFixture
{
    public IServiceProvider ServiceProvider { get; }

    public TestFixture()
    {
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // -------------------------
        // DbContext
        // -------------------------
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}");

            options.ConfigureWarnings(x =>
                x.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        });

        // -------------------------
        // Infrastructure
        // -------------------------
        services.AddDataLayerServices(configuration);
        services.AddBusinessLogicServices();

        // -------------------------
        // Fakes
        // -------------------------
        services.AddScoped<ICurrentUserService, FakeCurrentUserService>();

        services.AddLogging();

        ServiceProvider = services.BuildServiceProvider();

        Seed();
    }

    private void Seed()
    {
        using var scope = ServiceProvider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Set<ProductCategory>().Add(new ProductCategory
        {
            CategoryId = 1,
            CategoryName = "Test Category"
        });

        db.Set<ProductSubcategory>().Add(new ProductSubcategory
        {
            SubcategoryId = 1,
            CategoryId = 1,
            SubcategoryName = "Test Subcategory"
        });

        db.SaveChanges();
    }
}