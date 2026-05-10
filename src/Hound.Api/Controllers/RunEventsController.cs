using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Hound.Api.Hubs;
using Hound.Core.Models;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/runs/events")]
public class RunEventsController : ControllerBase
{
    private readonly IHubContext<ActivityHub> _hubContext;

    public RunEventsController(IHubContext<ActivityHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <summary>
    /// POST /api/runs/events/node-completed — Called by the trading-pack when a node finishes.
    /// Broadcasts the graph run snapshot to SignalR subscribers.
    /// </summary>
    [HttpPost("node-completed")]
    public async Task<IActionResult> NodeCompleted(
        [FromBody] GraphRun run,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group("pack-trading-pack")
            .SendAsync("OnGraphRunUpdate", run, cancellationToken);
        return Ok();
    }
}
