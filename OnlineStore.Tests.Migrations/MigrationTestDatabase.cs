using DataLayer.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace OnlineStore.Tests.Migrations;

/// <summary>
/// یک دیتابیس موقت واقعی روی SQL Server برای اجرای مایگریشن‌ها.
///
/// چرا SQLite کافی نیست: backfillهای این پروژه T-SQL‌اند
/// (<c>UPDATE ... FROM</c>، ایندکس فیلترشده) و SQLite آن‌ها را نمی‌فهمد.
/// تست مایگریشن روی موتور دیگری، تستِ چیز دیگری است.
/// </summary>
public sealed class MigrationTestDatabase : IAsyncDisposable
{
    private readonly string _masterConnectionString;
    private readonly string _databaseName;

    public string DatabaseName => _databaseName;

    public string ConnectionString =>
        new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = _databaseName
        }.ConnectionString;

    private MigrationTestDatabase(string masterConnectionString, string databaseName)
    {
        _masterConnectionString = masterConnectionString;
        _databaseName = databaseName;
    }

    public static async Task<MigrationTestDatabase> CreateAsync(string masterConnectionString)
    {
        var name = $"OnlineStore_MigTest_{Guid.NewGuid():N}";

        await using (var connection = new SqlConnection(masterConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}]";
            await command.ExecuteNonQueryAsync();
        }

        return new MigrationTestDatabase(masterConnectionString, name);
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            .Options;

        return new AppDbContext(options, Array.Empty<ISaveChangesInterceptor>());
    }

    /// <summary>مهاجرت به یک نسخهٔ مشخص (برای ساخت «اسکیمای قبلی»).</summary>
    public async Task MigrateToAsync(string migrationName)
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(migrationName);
    }

    public async Task MigrateAllAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task<int> ExecuteAsync(string sql)
    {
        await using var context = CreateContext();
        return await context.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var context = CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, typeof(T));
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = $@"
IF DB_ID('{_databaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{_databaseName}];
END";

        await command.ExecuteNonQueryAsync();
    }
}