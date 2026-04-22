using Hound.Api.Services;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IHealthCheckService _healthCheckService;

    public HealthController(IHealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    /// <summary>GET /api/health — Aggregated health status of all services.</summary>
    [HttpGet]
    public async Task<ActionResult<HealthReport>> GetHealth(CancellationToken cancellationToken)
    {
        var report = await _healthCheckService.CheckAllAsync(cancellationToken);
        return Ok(report);
    }
}
