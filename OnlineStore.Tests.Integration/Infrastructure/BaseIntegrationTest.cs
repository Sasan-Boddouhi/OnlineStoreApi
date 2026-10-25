using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Fixtures;

public abstract class BaseIntegrationTest
    : IClassFixture<IntegrationTestFactory<Program>>, IAsyncLifetime
{
    protected readonly IntegrationTestFactory<Program> Factory;
    protected readonly HttpClient Client;
    private IServiceScope? _scope;

    protected BaseIntegrationTest(IntegrationTestFactory<Program> factory)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Client = factory.CreateClient();
    }

    protected IServiceProvider Services =>
        _scope?.ServiceProvider ?? throw new InvalidOperationException("Scope not initialized");

    protected T GetService<T>() where T : notnull
        => Services.GetRequiredService<T>();

    public async Task InitializeAsync()
    {
        await Factory.InitializeDatabaseAsync();
        _scope = Factory.Services.CreateScope();
    }

    public async Task DisposeAsync()
    {
        if (_scope is IAsyncDisposable asyncScope)
            await asyncScope.DisposeAsync();
        else
            _scope?.Dispose();
        _scope = null;
    }
}