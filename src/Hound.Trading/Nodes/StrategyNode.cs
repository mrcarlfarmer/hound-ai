using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    private readonly ChatClientAgent _coordinatorAgent;
    private readonly ChatClientAgent _bullAgent;
    private readonly ChatClientAgent _bearAgent;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<StrategyNode>? _logger;
    private readonly StrategyHoundConfig _debateConfig;

    public StrategyNode(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        IOptions<StrategyHoundConfig>? debateConfig = null,
        ILoggerFactory? loggerFactory = null)
    {
        _alpacaService = alpacaService;
        _activityLogger = activityLogger;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<StrategyNode>();
        _debateConfig = debateConfig?.Value ?? new StrategyHoundConfig();

        _coordinatorAgent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are a JSON formatter that outputs trading decisions.
                You MUST respond with ONLY a single JSON object — no markdown, no explanation, no preamble.
                The JSON schema is:
                {"symbol":"AAPL","action":"Buy","quantity":10,"reasoning":"One paragraph explanation.","confidence":0.85,"trailPercent":5}

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

                TRAILING STOP RULES (Buy only):
                - Every Buy entry has a trailing-stop SELL attached by the
                  execution layer as the protective exit. The stop ratchets
                  UP with price and never DOWN, firing once price pulls back
                  by `trailPercent` from its high-water mark.
                - For action "Buy" you MUST include `trailPercent` (a number, NOT a string):
                  the trail offset expressed as a percentage of the
                  high-water mark. Valid range: 1 to 10. Tighter (1–3) when
                  you want to lock in gains aggressively; wider (5–10) to
                  give the position room to breathe through normal volatility.
                  Choose based on the symbol's recent volatility and the
                  analyst confidence.
                - For "Sell" and "Hold" omit `trailPercent` or set it to null.

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

                If a "Debate transcript" section is present you have already observed a
                short bull-vs-bear debate. Weigh both sides, decide which case is
                stronger, and produce the final JSON decision. The debate is advisory
                only — the price-sanity and decision rules above still apply.

                If you receive risk rejection feedback, adjust your decision to address
                the specific concerns and output the revised JSON.
                """,
            name: "StrategyNode",
            description: "Determines buy/sell/hold strategy based on market analysis",
            loggerFactory: loggerFactory);

        _bullAgent = new ChatClientAgent(
            chatClient,
            instructions: BullSystemPrompt,
            name: "BullDebater",
            description: "Argues the bullish case in the strategy debate",
            loggerFactory: loggerFactory);

        _bearAgent = new ChatClientAgent(
            chatClient,
            instructions: BearSystemPrompt,
            name: "BearDebater",
            description: "Argues the bearish case in the strategy debate",
            loggerFactory: loggerFactory);
    }

    private const string BullSystemPrompt = """
        You are the BULL debater in a short trading debate. Your only job is to argue
        the strongest, evidence-based case FOR buying or holding a long position in
        the symbol presented.

        Rules:
        - Respond in 2–4 sentences of plain English. No markdown, no JSON, no preamble.
        - Cite concrete signals from the analysis: trend, confidence, volume, key levels,
          ATR, fundamentals, news, sentiment.
        - If a previous BEAR argument is present, rebut its strongest point directly.
        - Never mention price levels that contradict the "Current price" in the prompt.
        - Do NOT propose a quantity, trail percent, or final action. The coordinator
          decides those after the debate.
        """;

    private const string BearSystemPrompt = """
        You are the BEAR debater in a short trading debate. Your only job is to argue
        the strongest, evidence-based case AGAINST buying — either for selling an
        existing long, or for holding off on a new entry.

        Rules:
        - Respond in 2–4 sentences of plain English. No markdown, no JSON, no preamble.
        - Cite concrete risks from the analysis: weak trend, low confidence, volume
          divergence, resistance overhead, fundamental concerns, negative news, bearish
          sentiment.
        - If a previous BULL argument is present, rebut its strongest point directly.
        - Never mention price levels that contradict the "Current price" in the prompt.
        - Do NOT propose a quantity, trail percent, or final action. The coordinator
          decides those after the debate.
        """;

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

        // Optional bull-vs-bear debate before the coordinator decides.
        IReadOnlyList<DebateTurn> debateTurns = Array.Empty<DebateTurn>();
        string coordinatorPrompt = BuildCoordinatorPrompt(state, prompt, debateTurns);

        if (_debateConfig.DebateEnabled && _debateConfig.DebateTurnsPerSide > 0)
        {
            debateTurns = await RunDebateAsync(state, prompt, cancellationToken);
            coordinatorPrompt = BuildCoordinatorPrompt(state, prompt, debateTurns);
        }

        var session = await _coordinatorAgent.CreateSessionAsync(cancellationToken);
        var response = await _coordinatorAgent.RunAsync(
            [new ChatMessage(ChatRole.User, coordinatorPrompt)],
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

        // Trailing-stop GTC is attached to every Buy by ExecutionNode as the
        // protective exit. Normalise TrailPercent here so downstream consumers
        // can rely on it: Buys get a clamped 1–10% value (defaulting to 5%
        // when the model omits it), and Sell/Hold always have it cleared so
        // dashboards don't surface a stale trail offset.
        decision = decision.Action switch
        {
            TradeAction.Buy => decision with { TrailPercent = ClampTrailPercent(decision.TrailPercent) },
            _ => decision with { TrailPercent = null },
        };

        // Surface the authoritative current price and the resulting notional
        // order value so downstream consumers (RiskNode, dashboard) don't have
        // to recompute them. EstimatedCost is null for Hold since there is no
        // order being placed.
        var currentPrice = analysis?.LastPrice;
        decimal? estimatedCost = decision.Action != TradeAction.Hold && currentPrice is decimal cp && cp > 0
            ? decision.Quantity * cp
            : null;
        decision = decision with { CurrentPrice = currentPrice, EstimatedCost = estimatedCost };

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
            // Structured fields let the dashboard render a "Coordinator
            // Verdict" banner at the foot of the live debate panel without
            // having to regex-parse the human-readable message above.
            Metadata = new Dictionary<string, object>
            {
                ["type"] = "strategy-decision",
                ["symbol"] = state.Symbol,
                ["runId"] = state.RunId,
                ["action"] = decision.Action.ToString(),
                ["quantity"] = decision.Quantity,
                ["confidence"] = decision.Confidence,
                ["debateEnabled"] = debateTurns.Count > 0,
                ["debateTurnCount"] = debateTurns.Count,
            },
        }, cancellationToken);

        return state with
        {
            StrategyOutput = decision,
            StrategyDebate = debateTurns.Count > 0 ? debateTurns : null,
        };
    }

    /// <summary>
    /// Runs a bounded round-robin bull-vs-bear debate. Each side speaks
    /// <see cref="StrategyHoundConfig.DebateTurnsPerSide"/> times in alternating
    /// order (Bull → Bear → Bull → Bear …). Captures each turn into a
    /// <see cref="DebateTurn"/> list and emits one <see cref="ActivityLog"/>
    /// per turn (with <c>type=debate-turn</c> metadata) so the dashboard can
    /// render the conversation live via the existing SignalR push.
    /// </summary>
    /// <remarks>
    /// Implemented as a direct loop over <see cref="ChatClientAgent.RunAsync"/>
    /// rather than via <c>AgentWorkflowBuilder.CreateGroupChatBuilderWith</c>:
    /// MAF 1.1.0's <c>RoundRobinGroupChatManager</c> termination is unreliable
    /// (see agent-framework issues #754 and #1560) and a manual loop matches
    /// the established pattern used by <see cref="AnalystsTeamNode"/>.
    /// </remarks>
    private async Task<IReadOnlyList<DebateTurn>> RunDebateAsync(
        TradingGraphState state, string analysisPrompt, CancellationToken cancellationToken)
    {
        var transcript = new List<DebateTurn>();
        int turnsPerSide = _debateConfig.DebateTurnsPerSide;

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "StrategyNode",
            Message = $"Debate starting for {state.Symbol} ({turnsPerSide} turn(s) per side)",
            Severity = ActivitySeverity.Info,
            Metadata = new Dictionary<string, object>
            {
                ["type"] = "debate-start",
                ["symbol"] = state.Symbol,
                ["runId"] = state.RunId,
                ["turnsPerSide"] = turnsPerSide,
            },
        }, cancellationToken);

        var seed = BuildDebateSeed(state, analysisPrompt);

        try
        {
            for (int round = 0; round < turnsPerSide; round++)
            {
                await AppendTurnAsync("Bull", round * 2, _bullAgent, seed, transcript, state.Symbol, state.RunId, cancellationToken);
                await AppendTurnAsync("Bear", round * 2 + 1, _bearAgent, seed, transcript, state.Symbol, state.RunId, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "Debate failed for {Symbol}; falling back to coordinator-only decision",
                state.Symbol);
            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = NodeId,
                HoundName = "StrategyNode",
                Message = $"Debate failed for {state.Symbol}: {ex.Message}. Falling back to coordinator-only path.",
                Severity = ActivitySeverity.Warning,
            }, cancellationToken);
            return Array.Empty<DebateTurn>();
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "StrategyNode",
            Message = $"Debate concluded after {transcript.Count} turn(s); coordinator deliberating for {state.Symbol}",
            Severity = ActivitySeverity.Info,
            Metadata = new Dictionary<string, object>
            {
                ["type"] = "debate-end",
                ["symbol"] = state.Symbol,
                ["runId"] = state.RunId,
                ["turnCount"] = transcript.Count,
            },
        }, cancellationToken);

        return transcript;
    }

    /// <summary>
    /// Runs a single debater turn and appends it to the transcript plus the
    /// activity log. The prompt is the original seed plus all preceding turns
    /// so debaters can rebut each other.
    /// </summary>
    private async Task AppendTurnAsync(
        string role,
        int index,
        ChatClientAgent agent,
        string seed,
        List<DebateTurn> transcript,
        string symbol,
        string runId,
        CancellationToken cancellationToken)
    {
        var prompt = BuildDebaterPrompt(role, seed, transcript);
        var session = await agent.CreateSessionAsync(cancellationToken);
        var response = await agent.RunAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            session,
            cancellationToken: cancellationToken);

        var text = (response.Text ?? string.Empty).Trim();
        var turn = new DebateTurn(role, index, text, DateTime.UtcNow);
        transcript.Add(turn);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "StrategyNode",
            Message = $"[{role.ToUpperInvariant()}] {Truncate(text, 240)}",
            Severity = ActivitySeverity.Info,
            Metadata = new Dictionary<string, object>
            {
                ["type"] = "debate-turn",
                ["role"] = role,
                ["turnIndex"] = index,
                ["symbol"] = symbol,
                ["runId"] = runId,
                ["fullMessage"] = text,
            },
        }, cancellationToken);
    }

    /// <summary>
    /// Builds the prompt for a debater turn: the original seed plus the
    /// preceding turns (so each debater can rebut the other).
    /// </summary>
    private static string BuildDebaterPrompt(string role, string seed, IReadOnlyList<DebateTurn> precedingTurns)
    {
        if (precedingTurns.Count == 0)
        {
            return $"{seed}\n\nIt is your turn ({role.ToUpperInvariant()}). Open the debate.";
        }

        var sb = new StringBuilder(seed);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Debate so far");
        foreach (var t in precedingTurns)
        {
            sb.Append('[').Append(t.Role.ToUpperInvariant()).Append("] ").AppendLine(t.Message);
        }
        sb.AppendLine();
        sb.Append("It is your turn (").Append(role.ToUpperInvariant()).AppendLine("). Make your strongest point, rebutting the previous turn if relevant.");
        return sb.ToString();
    }

    /// <summary>
    /// Wraps the original analysis prompt with the captured debate transcript
    /// so the coordinator can weigh both sides before emitting the final JSON.
    /// </summary>
    private static string BuildDebateSeed(TradingGraphState state, string analysisPrompt)
    {
        var sb = new StringBuilder();
        sb.Append("Symbol: ").AppendLine(state.Symbol);
        sb.AppendLine();
        sb.AppendLine("The following analysis snapshot is the basis for the debate. Argue your");
        sb.AppendLine("side in 2–4 sentences, citing concrete signals. Do not propose a final");
        sb.AppendLine("action.");
        sb.AppendLine();
        sb.AppendLine(analysisPrompt);

        AppendRiskRejectionContext(
            sb,
            state,
            headingPrefix: "## Risk rejection to address (attempt #",
            instruction: "You must directly address these rejected-risk concerns in your argument.");

        return sb.ToString();
    }

    private static string BuildCoordinatorPrompt(
        TradingGraphState state,
        string analysisPrompt,
        IReadOnlyList<DebateTurn> transcript)
    {
        var sb = new StringBuilder(analysisPrompt);

        AppendRiskRejectionContext(
            sb,
            state,
            headingPrefix: "## Previous risk rejection (attempt #",
            instruction: "Please address these risk concerns and adjust your strategy.");

        if (transcript.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Debate transcript");
            sb.AppendLine("Two debaters reviewed the analysis above. Weigh both sides, then output the JSON decision.");
            sb.AppendLine();
            foreach (var turn in transcript)
            {
                sb.Append('[').Append(turn.Role.ToUpperInvariant()).Append("] ").AppendLine(turn.Message);
            }
        }

        sb.AppendLine();
        sb.AppendLine("Now output ONLY the final JSON decision object.");
        return sb.ToString();
    }

    private static void AppendRiskRejectionContext(
        StringBuilder sb,
        TradingGraphState state,
        string headingPrefix,
        string instruction)
    {
        if (state.RefinementCount <= 0 || state.RiskOutput is null)
            return;

        sb.AppendLine();
        sb.Append(headingPrefix).Append(state.RefinementCount).AppendLine("):");
        sb.AppendLine(state.RiskOutput.Reasoning);
        sb.AppendLine(instruction);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";

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
    /// Default trail percent applied when the strategy hound emits a Buy
    /// without a <c>trailPercent</c> value. Chosen as a moderate exit cushion
    /// that fits the 1–10% valid band advertised in the prompt.
    /// </summary>
    internal const decimal DefaultBuyTrailPercent = 5m;
    internal const decimal MinTrailPercent = 1m;
    internal const decimal MaxTrailPercent = 10m;

    /// <summary>
    /// Clamps a model-supplied trail percent into the valid <c>1–10%</c>
    /// band, falling back to <see cref="DefaultBuyTrailPercent"/> when the
    /// value is missing or non-positive.
    /// </summary>
    internal static decimal ClampTrailPercent(decimal? value)
    {
        if (value is not decimal v || v <= 0m)
            return DefaultBuyTrailPercent;
        if (v < MinTrailPercent) return MinTrailPercent;
        if (v > MaxTrailPercent) return MaxTrailPercent;
        return v;
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
