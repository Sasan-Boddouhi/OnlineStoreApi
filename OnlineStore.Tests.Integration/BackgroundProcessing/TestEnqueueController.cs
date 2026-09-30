using Asp.Versioning;
using Application.BackgroundJobs;
using Microsoft.AspNetCore.Mvc;

namespace OnlineStore.Tests.Integration.BackgroundProcessing;

[ApiController]
[ApiVersionNeutral]
[Route("test/enqueue")]
public sealed class TestEnqueueController : ControllerBase
{
    private readonly IBackgroundJobQueue _queue;

    public TestEnqueueController(IBackgroundJobQueue queue) => _queue = queue;

    [HttpPost]
    public async Task<IActionResult> Enqueue()
    {
        var result = await _queue.EnqueueAsync(
            new TraceTestJob(42),
            $"trace-test-{Guid.NewGuid()}",
            HttpContext.RequestAborted);

        return result.IsAccepted
            ? Ok(new { accepted = true })
            : StatusCode(503, new { accepted = false });
    }
}

public sealed record TraceTestJob(int Value);