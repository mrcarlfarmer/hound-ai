using Alpaca.Markets;
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

    [HttpPost("positions/{symbol}/close")]
    public async Task<IActionResult> ClosePosition(
        string symbol,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await _alpaca.ClosePositionAsync(symbol, cancellationToken);
            return Ok(new
            {
                orderId = order.OrderId.ToString(),
                symbol = order.Symbol,
                side = order.OrderSide.ToString(),
                quantity = order.Quantity,
                status = order.OrderStatus.ToString(),
            });
        }
        catch (RestClientErrorException ex)
        {
            var (status, title, detail) = TranslateAlpacaError(ex, symbol);
            return Problem(
                statusCode: status,
                title: title,
                detail: detail,
                type: "https://docs.alpaca.markets/reference/errors");
        }
    }

    private static (int Status, string Title, string Detail) TranslateAlpacaError(
        RestClientErrorException ex,
        string symbol)
    {
        var message = ex.Message ?? string.Empty;
        var httpStatus = (int?)ex.HttpStatusCode ?? StatusCodes.Status502BadGateway;

        // Alpaca returns 404 when the position no longer exists on their side.
        if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound
            || message.Contains("position does not exist", StringComparison.OrdinalIgnoreCase)
            || message.Contains("position not found", StringComparison.OrdinalIgnoreCase))
        {
            return (
                StatusCodes.Status409Conflict,
                "Position already closed",
                $"No open {symbol} position was found on Alpaca. It may have already been liquidated outside Hound.");
        }

        // Insufficient quantity — the position exists but has 0 (or less than expected) shares free to sell.
        if (message.Contains("insufficient qty", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient quantity", StringComparison.OrdinalIgnoreCase))
        {
            return (
                StatusCodes.Status409Conflict,
                "Nothing left to close",
                $"Alpaca reports no available quantity for {symbol}. The position may already be closing or held by another open order. ({message})");
        }

        // Wash trade / day trading buying power / pattern day trader, etc.
        if (message.Contains("wash trade", StringComparison.OrdinalIgnoreCase)
            || message.Contains("pattern day trader", StringComparison.OrdinalIgnoreCase)
            || message.Contains("buying power", StringComparison.OrdinalIgnoreCase))
        {
            return (
                StatusCodes.Status422UnprocessableEntity,
                "Alpaca rejected the close order",
                message);
        }

        // Fallback — surface Alpaca's message so the user sees what happened.
        return (
            httpStatus is >= 400 and < 600 ? httpStatus : StatusCodes.Status502BadGateway,
            "Failed to close position",
            string.IsNullOrWhiteSpace(message) ? "Alpaca rejected the close request." : message);
    }
}
