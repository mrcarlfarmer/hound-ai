using Hound.Api.Hubs;
using Hound.Api.Repositories;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WatchtowerController : ControllerBase
{
    private readonly IWatchtowerRepository _repository;
    private readonly IHubContext<ActivityHub> _hubContext;

    public WatchtowerController(IWatchtowerRepository repository, IHubContext<ActivityHub> hubContext)
    {
        _repository = repository;
        _hubContext = hubContext;
    }

    /// <summary>
    /// POST /api/watchtower/webhook — Receives shoutrrr generic webhook notifications from Watchtower.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var message = payload.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
        var title = payload.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";

        var evt = new WatchtowerEvent
        {
            Action = title,
            ContainerName = ParseContainerName(message),
            ImageName = ParseImageName(message),
            OldImageId = ParseOldImageId(message),
            NewImageId = ParseNewImageId(message),
            Timestamp = DateTime.UtcNow,
            RawPayload = payload.GetRawText()
        };

        await _repository.StoreEventAsync(evt, cancellationToken);
        await _hubContext.Clients.All.SendAsync("OnWatchtowerEvent", evt, cancellationToken);

        return Ok();
    }

    /// <summary>
    /// GET /api/watchtower — Paginated watchtower events.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WatchtowerEvent>>> GetEvents(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var results = await _repository.GetEventsAsync(page, pageSize, cancellationToken);
        return Ok(results);
    }

    private static string ParseContainerName(string message)
    {
        // Watchtower report format: "- container_name (image:tag): ..."
        var match = System.Text.RegularExpressions.Regex.Match(message, @"- (\S+) \(");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string ParseImageName(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"\(([^)]+)\)");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string ParseOldImageId(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @":\s*(\w{7,12})\s+updated to");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string ParseNewImageId(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"updated to\s+(\w{7,12})");
        return match.Success ? match.Groups[1].Value : "";
    }
}
