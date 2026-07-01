using Microsoft.AspNetCore.Mvc;
using Hound.Core.Models;
using Hound.Api.Repositories;

namespace Hound.Api.Controllers;

/// <summary>
/// Serves persisted bull-vs-bear debate transcripts to the dashboard's
/// "Strategy Debate" panel. Reads dedicated <see cref="DebateRecord"/>
/// documents rather than reconstructing the transcript from per-turn
/// <c>ActivityLog</c> metadata.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DebatesController : ControllerBase
{
    private readonly IDebateRepository _debates;

    public DebatesController(IDebateRepository debates)
    {
        _debates = debates;
    }

    /// <summary>
    /// GET /api/debates/{runId} — All debate transcripts captured for a run,
    /// ordered by refinement iteration. Returns an empty array when the run has
    /// no persisted debates (e.g. debate disabled, or an older run predating
    /// this feature).
    /// </summary>
    [HttpGet("{runId}")]
    public async Task<ActionResult<IEnumerable<DebateRecord>>> GetDebates(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var records = await _debates.GetDebatesAsync(runId, cancellationToken);
        return Ok(records);
    }
}
