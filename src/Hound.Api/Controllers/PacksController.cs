using Microsoft.AspNetCore.Mvc;
using Hound.Core.Models;
using Hound.Api.Repositories;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PacksController : ControllerBase
{
    private readonly IPackRepository _packs;

    public PacksController(IPackRepository packs)
    {
        _packs = packs;
    }

    /// <summary>GET /api/packs — List all packs with metadata.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Pack>>> GetPacks(CancellationToken cancellationToken)
    {
        var packs = await _packs.GetAllPacksAsync(cancellationToken);
        return Ok(packs);
    }

    /// <summary>GET /api/packs/{id} — Single pack detail.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Pack>> GetPack(string id, CancellationToken cancellationToken)
    {
        var pack = await _packs.GetPackAsync(id, cancellationToken);
        if (pack is null)
            return NotFound();
        return Ok(pack);
    }

    /// <summary>GET /api/packs/{packId}/hounds — All hounds in a pack.</summary>
    [HttpGet("{packId}/hounds")]
    public async Task<ActionResult<IEnumerable<HoundInfo>>> GetHounds(string packId, CancellationToken cancellationToken)
    {
        var hounds = await _packs.GetHoundsAsync(packId, cancellationToken);
        return Ok(hounds);
    }

    /// <summary>POST /api/packs/register — Register or update a pack and its hounds.</summary>
    [HttpPost("register")]
    public async Task<ActionResult> Register([FromBody] PackRegistration registration, CancellationToken cancellationToken)
    {
        await _packs.RegisterPackAsync(registration, cancellationToken);
        return Ok();
    }
}
