using BusinessLogic.DTOs.Log;
using BusinessLogic.Services.Interfaces;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class LogServiceIntegrationTests : BaseIntegrationTest
{
    private ILogService LogService => GetService<ILogService>();

    public LogServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task GetPagedAsync_ShouldReturnPagedResult()
    {
        var filter = new LogFilterDto { PageNumber = 1, PageSize = 5 };
        var result = await LogService.GetPagedAsync(filter);
        result.Should().NotBeNull();
        result.Items.Should().NotBeNull();
    }
}