using Hound.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/portfolio")]
public class PortfolioController : ControllerBase
{
    private readonly IAlpacaPortfolioService _alpaca;

    public PortfolioController(IAlpacaPortfolioService alpaca)
    {
        _alpaca = alpaca;
    }

    [HttpGet("account")]
    public async Task<IActionResult> GetAccount(CancellationToken cancellationToken)
    {
        var account = await _alpaca.GetAccountAsync(cancellationToken);
        return Ok(new
        {
            equity = account.Equity,
            cash = account.TradableCash,
            buyingPower = account.BuyingPower,
            portfolioValue = account.Equity,
            dailyChangePercent = account.Equity != 0
                ? (account.Equity - account.LastEquity) / account.LastEquity * 100m
                : 0m,
            dailyChangeAmount = account.Equity - account.LastEquity,
            lastEquity = account.LastEquity,
            currency = account.Currency,
        });
    }

    [HttpGet("positions")]
    public async Task<IActionResult> GetPositions(CancellationToken cancellationToken)
    {
        var positions = await _alpaca.ListPositionsAsync(cancellationToken);
        var result = positions.Select(p => new
        {
            symbol = p.Symbol,
            quantity = p.Quantity,
            marketValue = p.MarketValue,
            currentPrice = p.AssetCurrentPrice,
            averageEntryPrice = p.AverageEntryPrice,
            unrealizedPl = p.UnrealizedProfitLoss,
            unrealizedPlPercent = p.UnrealizedProfitLossPercent,
            changeToday = p.AssetChangePercent,
            side = p.Side.ToString(),
        });
        return Ok(result);
    }
}
