using Microsoft.AspNetCore.Mvc;
using Hound.Core.Models;
using Hound.Api.Repositories;
using Hound.Api.Services;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RunsController : ControllerBase
{
    private readonly IGraphRunRepository _runs;
    private readonly DebateBackfillService _debateBackfill;

    public RunsController(IGraphRunRepository runs, DebateBackfillService debateBackfill)
    {
        _runs = runs;
        _debateBackfill = debateBackfill;
    }

    /// <summary>GET /api/runs — Recent graph runs (newest first).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GraphRun>>> GetRuns(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var runs = await _runs.GetRecentRunsAsync(limit, cancellationToken);
        await _debateBackfill.BackfillAsync(runs, cancellationToken);
        return Ok(runs);
    }

    /// <summary>GET /api/runs/{runId} — Single graph run detail with node snapshots.</summary>
    [HttpGet("{runId}")]
    public async Task<ActionResult<GraphRun>> GetRun(string runId, CancellationToken cancellationToken = default)
    {
        var run = await _runs.GetRunAsync(runId, cancellationToken);
        if (run is null)
            return NotFound();
        await _debateBackfill.BackfillAsync(run, cancellationToken);
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

    /// <summary>POST /api/runs/{runId}/approve — Approve a run that is awaiting human review.</summary>
    [HttpPost("{runId}/approve")]
    public async Task<IActionResult> Approve(
        string runId,
        [FromBody] ApprovalDecisionBody? body,
        CancellationToken cancellationToken = default)
    {
        return await SubmitDecisionAsync(runId, ApprovalStatus.Approved, body, cancellationToken);
    }

    /// <summary>POST /api/runs/{runId}/reject — Reject a run that is awaiting human review.</summary>
    [HttpPost("{runId}/reject")]
    public async Task<IActionResult> Reject(
        string runId,
        [FromBody] ApprovalDecisionBody? body,
        CancellationToken cancellationToken = default)
    {
        return await SubmitDecisionAsync(runId, ApprovalStatus.Rejected, body, cancellationToken);
    }

    private async Task<IActionResult> SubmitDecisionAsync(
        string runId,
        ApprovalStatus decision,
        ApprovalDecisionBody? body,
        CancellationToken cancellationToken)
    {
        var decidedBy = string.IsNullOrWhiteSpace(body?.DecidedBy) ? "user" : body!.DecidedBy!.Trim();
        var notes = string.IsNullOrWhiteSpace(body?.Notes) ? null : body!.Notes!.Trim();

        var applied = await _runs.SubmitApprovalAsync(runId, decision, decidedBy, notes, cancellationToken);
        if (!applied)
        {
            return Problem(
                statusCode: 409,
                title: "Run is not awaiting approval",
                detail: $"Run '{runId}' either does not exist or is not currently in the Pending approval state.");
        }

        return Accepted(new
        {
            runId,
            decision = decision.ToString(),
            decidedBy,
            notes,
        });
    }
}

public record RunRequestBody(string Symbol);

public record ApprovalDecisionBody(string? DecidedBy, string? Notes);
