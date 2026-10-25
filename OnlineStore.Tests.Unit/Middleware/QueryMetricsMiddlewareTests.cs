using System.Net;
using Application.Interfaces;
using Application.Middleware;
using Application.Models.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace OnlineStore.Tests.Unit.Middleware;

public class QueryMetricsMiddlewareTests
{
    private readonly Mock<RequestDelegate> _nextMock;
    private readonly Mock<ILogger<QueryMetricsMiddleware>> _loggerMock;
    private readonly Mock<IQueryMetricsService> _metricsServiceMock;
    private readonly QueryMetricsMiddleware _middleware;

    public QueryMetricsMiddlewareTests()
    {
        _nextMock = new Mock<RequestDelegate>();
        _loggerMock = new Mock<ILogger<QueryMetricsMiddleware>>();
        _metricsServiceMock = new Mock<IQueryMetricsService>();
        _middleware = new QueryMetricsMiddleware(_nextMock.Object, _loggerMock.Object);
    }

    private HttpContext CreateHttpContext(
        string path,
        string? filter = null,
        string? sort = null,
        string? userId = null,
        string? userName = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";

        var queryParams = new List<string>();

        if (filter != null)
            queryParams.Add($"filter={Uri.EscapeDataString(filter)}");

        if (sort != null)
            queryParams.Add($"sort={Uri.EscapeDataString(sort)}");

        if (queryParams.Count > 0)
            context.Request.QueryString = new QueryString("?" + string.Join("&", queryParams));

        var services = new ServiceCollection();
        services.AddSingleton(_metricsServiceMock.Object);

        var currentUserMock = new Mock<ICurrentUserService>();

        if (userId != null)
            currentUserMock.Setup(c => c.TryGetCurrentUserId()).Returns(int.Parse(userId));
        else
            currentUserMock.Setup(c => c.TryGetCurrentUserId()).Returns((int?)null);

        currentUserMock.Setup(c => c.GetCurrentUserName()).Returns(userName ?? string.Empty);

        services.AddSingleton(currentUserMock.Object);
        context.RequestServices = services.BuildServiceProvider();

        return context;
    }

    [Fact]
    public async Task InvokeAsync_WithFilterAndSort_LogsMetrics()
    {
        var context = CreateHttpContext(
            "/api/products",
            filter: "name eq 'test'",
            sort: "price desc",
            userId: "1",
            userName: "admin");

        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        await _middleware.InvokeAsync(context);

        _metricsServiceMock.Verify(m => m.LogAsync(It.Is<QueryMetrics>(
            q => q.Path == "/api/products"
                 && q.Filter == "name eq 'test'"
                 && q.Sort == "price desc"
                 && q.UserId == "1"
                 && q.UserName == "admin"
                 && q.ElapsedMilliseconds >= 0
        )), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionThrown_StillLogsMetricsAndRethrows()
    {
        var context = CreateHttpContext("/api/orders");
        var expectedException = new InvalidOperationException("test");

        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(expectedException);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _middleware.InvokeAsync(context));

        Assert.Same(expectedException, ex);

        _metricsServiceMock.Verify(m => m.LogAsync(It.Is<QueryMetrics>(
            q => q.HasException == true && q.Path == "/api/orders"
        )), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithoutUserService_StillLogsMetrics()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";

        var services = new ServiceCollection();
        services.AddSingleton(_metricsServiceMock.Object);
        context.RequestServices = services.BuildServiceProvider();

        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        await _middleware.InvokeAsync(context);

        _metricsServiceMock.Verify(m => m.LogAsync(It.Is<QueryMetrics>(
            q => q.UserId == null && q.UserName == null
        )), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithNoFilterOrSort_LogsMetricsWithZeros()
    {
        var context = CreateHttpContext("/api/test");

        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        await _middleware.InvokeAsync(context);

        _metricsServiceMock.Verify(m => m.LogAsync(It.Is<QueryMetrics>(
            q => q.Filter == null
                 && q.Sort == null
                 && q.FilterLength == 0
                 && q.SortFields == 0
                 && q.FilterConditions == 0
        )), Times.Once);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData(" ", 0)]
    [InlineData("name eq 'test'", 1)]
    [InlineData("name eq 'test' and price gt 10", 2)]
    [InlineData("name eq 'test' or price gt 10", 2)]
    public async Task CountConditions_CalculatesCorrectly(string filter, int expectedCount)
    {
        var context = CreateHttpContext("/api/test", filter: filter);

        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

        await _middleware.InvokeAsync(context);

        _metricsServiceMock.Verify(m => m.LogAsync(It.Is<QueryMetrics>(
            q => q.FilterConditions == expectedCount
        )), Times.Once);
    }
}