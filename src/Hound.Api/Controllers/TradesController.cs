using Hound.Api.Hubs;
using Hound.Api.Repositories;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/trades")]
public class TradesController : ControllerBase
{
    private readonly ITradeRepository _repository;
    private readonly IHubContext<ActivityHub> _hubContext;

    public TradesController(ITradeRepository repository, IHubContext<ActivityHub> hubContext)
    {
        _repository = repository;
        _hubContext = hubContext;
    }

    /// <summary>
    /// GET /api/trades — Paginated trade documents with optional filters.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TradeDocument>>> GetTrades(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? symbol = null,
        [FromQuery] FillStatus? fillStatus = null,
        CancellationToken cancellationToken = default)
    {
        var results = await _repository.GetTradesAsync(page, pageSize, symbol, fillStatus, cancellationToken);
        return Ok(results);
    }

    /// <summary>
    /// GET /api/trades/{id} — Single trade detail.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<TradeDocument>> GetTrade(
        string id,
        CancellationToken cancellationToken = default)
    {
        var trade = await _repository.GetTradeAsync(id, cancellationToken);
        if (trade is null)
            return NotFound();
        return Ok(trade);
    }

    /// <summary>
    /// POST /api/trades/order-update — Receives an order status update from the OrderWatcherService,
    /// persists the trade document, and broadcasts an <c>OnOrderUpdate</c> SignalR event.
    /// </summary>
    [HttpPost("order-update")]
    public async Task<IActionResult> PostOrderUpdate(
        [FromBody] TradeDocument trade,
        CancellationToken cancellationToken = default)
    {
        await _repository.UpsertTradeAsync(trade, cancellationToken);

        await _hubContext.Clients
            .Group("pack-trading-pack")
            .SendAsync("OnOrderUpdate", new
            {
                tradeDocumentId = trade.Id,
                symbol = trade.Symbol,
                fillStatus = trade.FillStatus.ToString(),
                filledQuantity = trade.FilledQuantity,
                averageFillPrice = trade.AverageFillPrice,
                executionTime = trade.ExecutionTime,
            }, cancellationToken);

        return Ok();
    }
}
