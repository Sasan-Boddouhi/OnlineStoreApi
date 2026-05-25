using Microsoft.Data.SqlClient;
using Respawn;
using Respawn.Graph;
using Xunit;

namespace OnlineStore.Tests.Integration.Infrastructure;

public class DatabaseRespawner : IAsyncLifetime
{
    private readonly string _connectionString;
    private Respawner _respawner = null!;

    public DatabaseRespawner(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        using var connection = new SqlConnection(_connectionString);

        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            TablesToIgnore = new[]
            {
                new Table("__EFMigrationsHistory")
            }
        });
    }

    public async Task ResetAsync()
    {
        using var connection = new SqlConnection(_connectionString);

        await connection.OpenAsync();

        await _respawner.ResetAsync(connection);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}