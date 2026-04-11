using Microsoft.AspNetCore.Mvc;
using Hound.Core.Models;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PacksController : ControllerBase
{
    /// <summary>GET /api/packs — List all packs with metadata.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<Pack>> GetPacks()
    {
        // TODO: Wave 2 — query RavenDB for registered packs
        return Ok(Array.Empty<Pack>());
    }

    /// <summary>GET /api/packs/{id} — Single pack detail.</summary>
    [HttpGet("{id}")]
    public ActionResult<Pack> GetPack(string id)
    {
        // TODO: Wave 2
        return NotFound();
    }

    /// <summary>GET /api/packs/{packId}/hounds — All hounds in a pack.</summary>
    [HttpGet("{packId}/hounds")]
    public ActionResult<IEnumerable<HoundInfo>> GetHounds(string packId)
    {
        // TODO: Wave 2
        return Ok(Array.Empty<HoundInfo>());
    }
}
