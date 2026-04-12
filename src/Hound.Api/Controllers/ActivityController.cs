using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Hound.Api.Hubs;
using Hound.Core.Logging;
using Hound.Core.Models;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActivityController : ControllerBase
{
    private readonly IActivityLogger _activityLogger;
    private readonly IHubContext<ActivityHub> _hubContext;

    public ActivityController(IActivityLogger activityLogger, IHubContext<ActivityHub> hubContext)
    {
        _activityLogger = activityLogger;
        _hubContext = hubContext;
    }

    /// <summary>
    /// GET /api/activity — Paginated activity log with filters.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ActivityLog>>> GetActivity(
        [FromQuery] string? pack = null,
        [FromQuery] string? hound = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var results = await _activityLogger.GetActivitiesAsync(
            packId: pack,
            houndId: hound,
            from: from,
            to: to,
            page: page,
            pageSize: pageSize,
            cancellationToken: cancellationToken);
        return Ok(results);
    }

    /// <summary>
    /// POST /api/activity — Persists an activity log entry and broadcasts it to
    /// all SignalR clients subscribed to the relevant pack group.
    /// Called by pack containers (e.g. trading-pack) to hook into the eventing framework.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PostActivity(
        [FromBody] ActivityLog activity,
        CancellationToken cancellationToken = default)
    {
        await _activityLogger.LogActivityAsync(activity, cancellationToken);
        await _hubContext.Clients
            .Group($"pack-{activity.PackId}")
            .SendAsync("OnActivity", activity, cancellationToken);
        return Ok();
    }
}
