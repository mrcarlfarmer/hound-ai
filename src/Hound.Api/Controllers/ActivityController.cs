using Microsoft.AspNetCore.Mvc;
using Hound.Core.Models;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActivityController : ControllerBase
{
    /// <summary>
    /// GET /api/activity — Paginated activity log with filters.
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<ActivityLog>> GetActivity(
        [FromQuery] string? pack = null,
        [FromQuery] string? hound = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        // TODO: Wave 2 — query RavenDB with filters, pagination
        return Ok(Array.Empty<ActivityLog>());
    }
}
