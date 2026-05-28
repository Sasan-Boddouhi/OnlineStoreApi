using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using FluentAssertions;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Fixtures;

[Collection("DatabaseCollection")]
public abstract class BaseIntegrationTest
    : IClassFixture<IntegrationTestFactory<Program>>, IAsyncLifetime
{
    protected readonly IntegrationTestFactory<Program> Factory;
    protected readonly HttpClient Client;

    private IServiceScope _scope = null!;

    protected BaseIntegrationTest(IntegrationTestFactory<Program> factory)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Client = factory.CreateClient();
    }

    protected T GetService<T>() where T : notnull
    {
        return _scope.ServiceProvider.GetRequiredService<T>();
    }

    public async Task InitializeAsync()
    {
        await Factory.InitializeDatabaseAsync();
        _scope = Factory.Services.CreateScope();
    }

    public async Task DisposeAsync()
    {
        _scope?.Dispose();
        await Task.CompletedTask;
    }

    protected async Task<T?> ReadAsync<T>(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.Content.ReadFromJsonAsync<T>();
    }
}