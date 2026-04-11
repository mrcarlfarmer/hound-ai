using Microsoft.AspNetCore.Mvc;
using Hound.Core.Logging;
using Hound.Core.Models;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActivityController : ControllerBase
{
    private readonly IActivityLogger _activityLogger;

    public ActivityController(IActivityLogger activityLogger)
    {
        _activityLogger = activityLogger;
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
}
