using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hound.Trading.Nodes;

/// <summary>
/// Determines trading strategy based on market context from AnalystsTeamNode.
/// On refinement loops, incorporates RiskNode rejection reasoning as additional context.
/// Uses <c>qwen3:14b</c> via the <c>"strategy"</c> keyed IChatClient.
/// </summary>
public class StrategyNode : INode
{
    public string NodeId => "strategy-node";
    public string PackId => "trading-pack";

    /// <summary>
    /// Maximum allowed drift of any dollar figure mentioned in the model's
    /// reasoning relative to the authoritative <c>LastPrice</c>. Levels outside
    /// this band are flagged as likely hallucinations (recommendation #7).
    /// </summary>
    private const decimal PriceSanityBandFraction = 0.20m;

    private static readonly Regex DollarFigureRegex = new(
        @"\$\s?(\d{1,3}(?:[,]\d{3})*(?:\.\d+)?|\d+(?:\.\d+)?)",
        RegexOptions.Compiled);

    private readonly ChatClientAgent _agent;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;
    private readonly ILogger<StrategyNode>? _logger;

    public StrategyNode(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
    {
        _alpacaService = alpacaService;
        _activityLogger = activityLogger;
        _logger = loggerFactory?.CreateLogger<StrategyNode>();

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are a JSON formatter that outputs trading decisions.
                You MUST respond with ONLY a single JSON object — no markdown, no explanation, no preamble.
                The JSON schema is:
                {"symbol":"AAPL","action":"Buy","quantity":10,"reasoning":"One paragraph explanation.","confidence":0.85}

                LANGUAGE: The `reasoning` field MUST be written in ENGLISH. Do not
                use Chinese, Mandarin, or any other non-English characters. Prices
                are in US dollars (USD, $) — never yuan/元, euro, or any other currency.

                Decision rules:
                - Confidence >= 0.7 and Bullish trend => action "Buy"
                - Confidence >= 0.7 and Bearish trend => action "Sell"
                - Otherwise => action "Hold"
                - action must be exactly one of: "Buy", "Sell", "Hold"
                - quantity must be a positive number for Buy/Sell (minimum 0.001), 0 for Hold
                - Fractional shares ARE supported. If the suggested max is less than one
                  whole share you MUST still propose a fractional position (e.g. 0.25)
                  rather than defaulting to Hold purely because of size. Only choose
                  Hold when the analysis itself does not justify a trade.
                - For Buy/Sell, quantity must NOT exceed the "Suggested max shares" value supplied in the prompt
                - Round quantity to at most 4 decimal places
                - confidence is 0.0 to 1.0
                - reasoning is a single paragraph, no markdown formatting
                - Output raw JSON only. No ```json fences, no markdown, no extra text.

                PRICE SANITY RULES — STRICTLY ENFORCED:
                - The authoritative current price for this symbol is the "Current price" value supplied
                  in the prompt header. This is the ONLY source of truth for the symbol's price.
                - Any price level you mention in `reasoning` (entry, target, stop-loss,
                  support, resistance) MUST be within ±15% of the current price. Do NOT
                  invent round-number levels (e.g. $80/$90/$100) that contradict the
                  current price.
                - The "Analysis snapshot" JSON may contain values from prior analyst
                  discussions; treat them as advisory commentary only. If anything in
                  the snapshot contradicts the current price, IGNORE the snapshot value
                  and use the current price.
                - Do not output any price level for a different ticker, even if it
                  looks similar.

                If you receive risk rejection feedback, adjust your decision to address
                the specific concerns and output the revised JSON.
                """,
            name: "StrategyNode",
            description: "Determines buy/sell/hold strategy based on market analysis",
            loggerFactory: loggerFactory);
    }

    public async Task<TradingGraphState> ExecuteAsync(
        TradingGraphState state, CancellationToken cancellationToken)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "StrategyNode",
            Message = $"Determining strategy for {state.Symbol}" +
                      (state.RefinementCount > 0 ? $" (refinement #{state.RefinementCount})" : string.Empty),
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var analysis = state.DataOutput;

        // Pull account context so we can give the model real buying-power
        // numbers and a suggested max share cap. Without this it has nothing
        // to anchor `quantity` on and frequently emits 0, which downstream
        // gets coerced to Hold (recommendation #4).
        decimal? equity = null, buyingPower = null, cash = null;
        try
        {
            var account = await _alpacaService.GetAccountAsync(cancellationToken);
            equity = account.Equity;
            buyingPower = account.BuyingPower;
            cash = account.TradableCash;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "StrategyNode failed to fetch account context for {Symbol}", state.Symbol);
        }

        var prompt = BuildPrompt(state, analysis, equity, buyingPower, cash);

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            session,
            cancellationToken: cancellationToken);

        var json = LlmResponseParser.ExtractJson(response.Text ?? "{}");
        TradingDecision decision;

        try
        {
            var result = JsonSerializer.Deserialize<TradingDecision>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
            decision = result ?? new TradingDecision(state.Symbol, TradeAction.Hold, 0, "No decision", 0);
        }
        catch (JsonException)
        {
            decision = new TradingDecision(state.Symbol, TradeAction.Hold, 0, json, 0);
        }

        // Treat Buy/Sell with zero quantity as Hold to avoid pointless refinement loops
        if (decision.Action != TradeAction.Hold && decision.Quantity <= 0)
        {
            decision = decision with { Action = TradeAction.Hold, Reasoning = $"Converted to Hold: {decision.Action} with quantity 0 is not actionable. {decision.Reasoning}" };
        }

        // Price-sanity check (recommendation #7): scan reasoning for dollar
        // figures and flag any that fall outside ±20% of LastPrice. We don't
        // re-prompt (latency cost) — we annotate the reasoning so the dashboard
        // and Risk node can see the flag, and we emit a warning activity.
        if (analysis?.LastPrice is decimal lastPrice && lastPrice > 0 && !string.IsNullOrEmpty(decision.Reasoning))
        {
            var violations = FindPriceSanityViolations(decision.Reasoning, lastPrice).ToList();
            if (violations.Count > 0)
            {
                var warning =
                    $"PRICE-SANITY WARNING: {violations.Count} dollar figure(s) in reasoning fall outside ±{PriceSanityBandFraction:P0} of current price ${lastPrice:F2}: " +
                    string.Join(", ", violations.Select(v => $"${v:F2}")) +
                    ". These levels may be hallucinated; do not act on them.";

                _logger?.LogWarning(
                    "StrategyNode price-sanity violation for {Symbol}: lastPrice={LastPrice}, offending={Offending}",
                    state.Symbol, lastPrice, string.Join(",", violations));

                await _activityLogger.LogActivityAsync(new ActivityLog
                {
                    PackId = PackId,
                    HoundId = NodeId,
                    HoundName = "StrategyNode",
                    Message = warning,
                    Severity = ActivitySeverity.Warning,
                }, cancellationToken);

                decision = decision with { Reasoning = $"{decision.Reasoning}\n\n[{warning}]" };
            }
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "StrategyNode",
            Message = $"Decision for {state.Symbol}: {decision.Action} (confidence {decision.Confidence:P0})",
            Severity = ActivitySeverity.Success,
        }, cancellationToken);

        return state with { StrategyOutput = decision };
    }

    /// <summary>
    /// Builds a structured, plain-text prompt that surfaces the authoritative
    /// price + account context before the analysis JSON, and strips the verbose
    /// analyst markdown reports out of the JSON (recommendations #1, #6).
    /// </summary>
    private static string BuildPrompt(
        TradingGraphState state,
        MarketAnalysis? analysis,
        decimal? equity,
        decimal? buyingPower,
        decimal? cash)
    {
        var sb = new StringBuilder();
        var symbol = analysis?.Symbol ?? state.Symbol;
        var companyName = analysis?.CompanyName;
        var lastPrice = analysis?.LastPrice;
        var trend = analysis?.Trend ?? "Unknown";
        var confidence = analysis?.ConfidenceScore;
        var volumeChange = analysis?.VolumeChange;

        sb.AppendLine("## Decision context (authoritative)");
        sb.Append("Symbol: ").Append(symbol);
        if (!string.IsNullOrWhiteSpace(companyName))
            sb.Append(" (").Append(companyName).Append(')');
        sb.AppendLine();

        sb.Append("Current price: ");
        sb.AppendLine(lastPrice is decimal lp
            ? $"${lp.ToString("F2", CultureInfo.InvariantCulture)} (the ONLY source of truth for this symbol's price)"
            : "UNAVAILABLE (no broker data — strongly prefer Hold)");

        sb.Append("Trend: ").AppendLine(trend);
        sb.Append("Analyst confidence: ").AppendLine(confidence is double c
            ? c.ToString("0.00", CultureInfo.InvariantCulture)
            : "n/a");
        sb.Append("Volume change vs 20-day avg: ").AppendLine(volumeChange is decimal v
            ? v.ToString("0.00", CultureInfo.InvariantCulture) + "x"
            : "n/a");

        sb.AppendLine();
        sb.AppendLine("## Account context");
        sb.Append("Equity: ").AppendLine(equity is decimal e
            ? $"${e.ToString("F2", CultureInfo.InvariantCulture)}"
            : "n/a");
        sb.Append("Buying power: ").AppendLine(buyingPower is decimal bp
            ? $"${bp.ToString("F2", CultureInfo.InvariantCulture)}"
            : "n/a");
        sb.Append("Tradable cash: ").AppendLine(cash is decimal ca
            ? $"${ca.ToString("F2", CultureInfo.InvariantCulture)}"
            : "n/a");

        // Suggested cap: at most 30% of buying power or 20% of equity per
        // single position. We express this both as a notional dollar amount
        // and as a fractional share count (rounded to 4 dp) so the model can
        // still propose a sensible position when the cap is below the price
        // of one whole share. Whole-share rounding here was the historical
        // cause of "strong Buy => Hold" outcomes on small accounts.
        // This is just an anchor for the model — the Risk node enforces final
        // sizing constraints, and ExecutionNode verifies fractionable support.
        if (lastPrice is decimal price && price > 0)
        {
            var (dollarCap, maxShares) = CalculateSuggestedCap(price, equity, buyingPower);
            if (dollarCap is decimal cap && cap > 0 && maxShares is decimal shares && shares > 0)
            {
                sb.Append("Suggested max shares for a new position: ")
                  .Append(shares.ToString("0.####", CultureInfo.InvariantCulture))
                  .Append(" (~$")
                  .Append(cap.ToString("F2", CultureInfo.InvariantCulture))
                  .AppendLine(" notional). Fractional shares are allowed; do NOT exceed this for Buy/Sell.");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Analysis snapshot (advisory only — IGNORE any price levels that conflict with the current price above)");
        // Slimmed-down JSON: drops MarketReport/FundamentalsReport/NewsReport/
        // SentimentReport so the model isn't tempted to parrot hallucinated
        // levels from those free-form analyst write-ups.
        var slim = new
        {
            symbol,
            companyName,
            lastPrice,
            volumeChange,
            trend,
            confidenceScore = confidence,
            summary = analysis?.Summary,
            indicators = analysis?.Indicators,
        };
        sb.AppendLine(JsonSerializer.Serialize(slim, new JsonSerializerOptions { WriteIndented = false }));

        sb.AppendLine();
        sb.AppendLine("What is your trading decision? Output the JSON object now.");

        // On refinement loops, inject the risk rejection reasoning
        if (state.RefinementCount > 0 && state.RiskOutput is not null)
        {
            sb.AppendLine();
            sb.Append("## Previous risk rejection (attempt #").Append(state.RefinementCount).AppendLine("):");
            sb.AppendLine(state.RiskOutput.Reasoning);
            sb.AppendLine("Please address these risk concerns and adjust your strategy.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Computes the suggested per-position cap as a dollar amount and a
    /// fractional share count (4 dp). The cap is the smaller of 30% of
    /// buying power and 20% of equity. Returns a non-null share count
    /// whenever the dollar cap is positive, even when it is less than the
    /// price of a single whole share — this is the key behaviour that lets
    /// the strategy hound propose fractional positions on small accounts
    /// instead of silently falling back to Hold.
    /// </summary>
    internal static (decimal? DollarCap, decimal? MaxShares) CalculateSuggestedCap(
        decimal price, decimal? equity, decimal? buyingPower)
    {
        if (price <= 0)
            return (null, null);

        var bpCap = buyingPower is decimal bpv ? bpv * 0.30m : (decimal?)null;
        var eqCap = equity is decimal eqv ? eqv * 0.20m : (decimal?)null;
        decimal? dollarCap = (bpCap, eqCap) switch
        {
            (decimal b, decimal eq) => Math.Min(b, eq),
            (decimal b, null) => b,
            (null, decimal eq) => eq,
            _ => null,
        };

        if (dollarCap is not decimal cap || cap <= 0)
            return (dollarCap, null);

        // Round DOWN to 4 dp so we never suggest a quantity that would
        // exceed the dollar cap when multiplied back by price.
        var shares = Math.Floor(cap / price * 10000m) / 10000m;
        return (cap, shares);
    }

    /// <summary>
    /// Returns any dollar figures in <paramref name="reasoning"/> that fall
    /// outside ±<see cref="PriceSanityBandFraction"/> of
    /// <paramref name="lastPrice"/>. Very small figures (&lt; $1) are skipped
    /// because they are typically per-share P&amp;L or fractional commentary,
    /// not price levels. Very large figures (&gt; 100 × lastPrice) are also
    /// skipped because they are typically dollar notional / portfolio amounts.
    /// </summary>
    private static IEnumerable<decimal> FindPriceSanityViolations(string reasoning, decimal lastPrice)
    {
        var lower = lastPrice * (1m - PriceSanityBandFraction);
        var upper = lastPrice * (1m + PriceSanityBandFraction);
        var notionalCutoff = lastPrice * 100m;

        foreach (Match m in DollarFigureRegex.Matches(reasoning))
        {
            var raw = m.Groups[1].Value.Replace(",", string.Empty);
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                continue;
            if (value < 1m || value > notionalCutoff)
                continue;
            if (value < lower || value > upper)
                yield return value;
        }
    }
}
