using Microsoft.AspNetCore.Mvc;
using Hound.Core.Models;
using Hound.Api.Repositories;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RunsController : ControllerBase
{
    private readonly IGraphRunRepository _runs;

    public RunsController(IGraphRunRepository runs)
    {
        _runs = runs;
    }

    /// <summary>GET /api/runs — Recent graph runs (newest first).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GraphRun>>> GetRuns(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var runs = await _runs.GetRecentRunsAsync(limit, cancellationToken);
        return Ok(runs);
    }

    /// <summary>GET /api/runs/{runId} — Single graph run detail with node snapshots.</summary>
    [HttpGet("{runId}")]
    public async Task<ActionResult<GraphRun>> GetRun(string runId, CancellationToken cancellationToken = default)
    {
        var run = await _runs.GetRunAsync(runId, cancellationToken);
        if (run is null)
            return NotFound();
        return Ok(run);
    }

    /// <summary>POST /api/runs — Queue a graph run for a ticker symbol.</summary>
    [HttpPost]
    public async Task<ActionResult<RunRequest>> QueueRun(
        [FromBody] RunRequestBody body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body.Symbol))
            return BadRequest("Symbol is required.");

        var symbol = body.Symbol.Trim().ToUpperInvariant();
        var request = await _runs.QueueRunRequestAsync(symbol, cancellationToken);
        return Ok(request);
    }

    /// <summary>GET /api/runs/requests — Recent run requests.</summary>
    [HttpGet("requests")]
    public async Task<ActionResult<IEnumerable<RunRequest>>> GetRequests(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var requests = await _runs.GetRecentRequestsAsync(limit, cancellationToken);
        return Ok(requests);
    }
}

public record RunRequestBody(string Symbol);
