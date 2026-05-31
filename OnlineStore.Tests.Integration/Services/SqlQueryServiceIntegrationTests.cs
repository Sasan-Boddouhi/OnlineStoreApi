using Application.Interfaces;
using DataLayer.Context;
using DataLayer.Services;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class SqlQueryServiceIntegrationTests : BaseIntegrationTest
{
    private AppDbContext DbContext => GetService<AppDbContext>();
    private ISqlQueryService SqlQueryService => new SqlQueryService(DbContext);

    public SqlQueryServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private class TestSqlResult : ISqlResult
    {
        public string? CategoryName { get; set; }
    }

    [Fact]
    public async Task QueryAsync_ShouldReturnResults()
    {
        var sql = "SELECT 'Electronics' AS CategoryName";
        var results = await SqlQueryService.QueryAsync<TestSqlResult>(sql);

        results.Should().NotBeNull();
        results.Should().HaveCount(1);
        results[0].CategoryName.Should().Be("Electronics");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnAffectedRows()
    {
        var createSql = "CREATE TABLE TempTest (Id INTEGER PRIMARY KEY, Value TEXT)";
        await SqlQueryService.ExecuteAsync(createSql);

        var insertSql = "INSERT INTO TempTest (Value) VALUES ('Hello')";
        var affected = await SqlQueryService.ExecuteAsync(insertSql);

        affected.Should().Be(1);

        // Clean up
        await SqlQueryService.ExecuteAsync("DROP TABLE TempTest");
    }
}