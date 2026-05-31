using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hound.Trading.Nodes;

/// <summary>
/// Analyst team node inspired by TradingAgents. Runs four specialist analysts
/// sequentially — Market, Fundamentals, News, Sentiment — then synthesises
/// their reports into a single <see cref="MarketAnalysis"/>.
/// </summary>
public class AnalystsTeamNode : INode
{
    public string NodeId => "analysts-team-node";
    public string PackId => "trading-pack";

    private readonly ChatClientAgent _marketAnalyst;
    private readonly ChatClientAgent _fundamentalsAnalyst;
    private readonly ChatClientAgent _newsAnalyst;
    private readonly ChatClientAgent _sentimentAnalyst;
    private readonly ChatClientAgent _synthesiser;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;

    public AnalystsTeamNode(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
    {
        _alpacaService = alpacaService;
        _activityLogger = activityLogger;

        // ── Market Analyst ────────────────────────────────────────────────────
        // NOTE: tools are bound via method-group delegates rather than
        // attributed lambdas. AIFunctionFactory's reflection invoker has a bug
        // in 10.4.x that mangles `NullableContextAttribute` (loads it as
        // `lableContextAttribute`) when invoking synthesised lambda closures
        // with `[Description]` parameter attributes, which breaks the first
        // tool call in the analysts pipeline.
        var marketTools = new List<AITool>
        {
            AIFunctionFactory.Create(
                FetchStockDataAsync,
                "get_stock_data",
                "Retrieves OHLCV price bars for a symbol in a date range"),
        };

        _marketAnalyst = new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a trading assistant tasked with analysing financial markets.
                Use the get_stock_data tool to retrieve recent OHLCV price bars.

                LANGUAGE: Write your entire report in ENGLISH. Do not use Chinese,
                Mandarin, or any other non-English characters. Prices are in US
                dollars (USD, $) — never yuan/元, euro, or any other currency.

                CRITICAL DATA-TRUST RULES:
                - The get_stock_data tool returns AUTHORITATIVE live market data from
                  the broker API. Trust it completely.
                - Your training cutoff is irrelevant. If a date looks "future" to you
                  but the tool returned rows for it, the data is real. Do NOT claim
                  the data is unavailable, simulated, or in the future.
                - The only time data is unavailable is when the tool explicitly
                  returns a message starting with "NO_DATA" or "ERROR". In every
                  other case, the bars are real and must be analysed as-is.

                PRICE-LEVEL RULES — STRICTLY ENFORCED:
                - The user prompt supplies "Current price" — this is the authoritative
                  last close.
                - Any price level you mention (support, resistance, moving averages,
                  targets, stops) MUST be derived from the actual bars returned by
                  the tool and MUST be within ±15% of the current price.
                - Do NOT invent round-number levels (e.g. $80, $90, $100) that
                  contradict the current price. Do NOT cite levels for a different
                  ticker.

                Analyse the data for trend direction, momentum, and volume patterns.
                Select and discuss the most relevant technical observations (moving averages,
                support/resistance, RSI-like momentum, volume changes, volatility).
                Write a detailed report with specific, actionable insights.
                End with a Markdown table summarising key observations.
                """,
            name: "MarketAnalyst",
            description: "Technical market analyst — price action and indicators",
            tools: marketTools,
            loggerFactory: loggerFactory);

        // ── Fundamentals Analyst ──────────────────────────────────────────────
        var fundamentalsTools = new List<AITool>
        {
            AIFunctionFactory.Create(
                FetchFundamentalsAsync,
                "get_fundamentals",
                "Retrieves company fundamentals — profile, financials, account equity"),
        };

        _fundamentalsAnalyst = new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a fundamentals analyst tasked with analysing company financial data.
                Use the get_fundamentals tool to retrieve available data.
                Analyse the account equity, buying power, and any position data.
                Write a comprehensive report on the financial context relevant for trading.
                Include assessment of capital availability and portfolio exposure.

                LANGUAGE: Write your entire report in ENGLISH. Do not use Chinese,
                Mandarin, or any other non-English characters. Prices are in US
                dollars (USD, $) — never yuan/元, euro, or any other currency.

                End with a Markdown table summarising key financial metrics.
                """,
            name: "FundamentalsAnalyst",
            description: "Fundamentals analyst — financial data and company profile",
            tools: fundamentalsTools,
            loggerFactory: loggerFactory);

        // ── News Analyst ──────────────────────────────────────────────────────
        var newsTools = new List<AITool>
        {
            AIFunctionFactory.Create(
                FetchNewsAsync,
                "get_news",
                "Retrieves recent news and market events for a symbol"),
        };

        _newsAnalyst = new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a news analyst tasked with analysing recent news and market trends.
                Use the get_news tool to retrieve recent news for the company.
                Analyse the news for impact on trading — earnings, regulatory changes,
                sector trends, and macroeconomic factors.
                Provide specific, actionable insights with supporting evidence.

                LANGUAGE: Write your entire report in ENGLISH. Do not use Chinese,
                Mandarin, or any other non-English characters. Prices are in US
                dollars (USD, $) — never yuan/元, euro, or any other currency.

                PRICE-LEVEL RULES — STRICTLY ENFORCED:
                - The user prompt supplies "Current price" — this is the authoritative
                  last close. Anchor all commentary to it.
                - Do NOT invent specific price targets, support, or resistance levels.
                  If you must reference a level, it MUST be within ±15% of the
                  current price.
                - Do NOT confuse the ticker with a similarly-spelled symbol.

                End with a Markdown table summarising key news items and their expected impact.
                """,
            name: "NewsAnalyst",
            description: "News analyst — macro and company-specific news impact",
            tools: newsTools,
            loggerFactory: loggerFactory);

        // ── Sentiment Analyst ─────────────────────────────────────────────────
        var sentimentTools = new List<AITool>
        {
            AIFunctionFactory.Create(
                FetchSentimentAsync,
                "get_sentiment",
                "Retrieves social media sentiment and public opinion for a symbol"),
        };

        _sentimentAnalyst = new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a social media and sentiment analyst.
                Use the get_sentiment tool to retrieve sentiment data for the company.
                Analyse public sentiment, social media trends, and market mood.
                Assess whether sentiment is bullish, bearish, or neutral.
                Provide specific insights on how sentiment may affect near-term trading.

                LANGUAGE: Write your entire report in ENGLISH. Do not use Chinese,
                Mandarin, or any other non-English characters. Prices are in US
                dollars (USD, $) — never yuan/元, euro, or any other currency.

                PRICE-LEVEL RULES — STRICTLY ENFORCED:
                - The user prompt supplies "Current price" — anchor all commentary to it.
                - Do NOT invent specific price targets, support, or resistance levels.
                  If you must reference a level, it MUST be within ±15% of the
                  current price.
                - Do NOT confuse the ticker with a similarly-spelled symbol.

                End with a Markdown table summarising sentiment indicators.
                """,
            name: "SentimentAnalyst",
            description: "Sentiment analyst — social media and public opinion",
            tools: sentimentTools,
            loggerFactory: loggerFactory);

        // ── Synthesiser ───────────────────────────────────────────────────────
        _synthesiser = new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a JSON formatter. You receive four analyst reports and extract key metrics.
                You MUST respond with ONLY a single JSON object — no markdown, no explanation, no preamble.
                The JSON schema is:
                {"symbol":"MSFT","lastPrice":425.30,"volumeChange":1.15,"trend":"Bullish","confidenceScore":0.75,"summary":"Two sentence synthesis."}

                Rules:
                - lastPrice = the most recent closing price from the market report. Must be > 0.
                - volumeChange = volume ratio from the market report (e.g. 1.2 means 20% above average). Must be > 0.
                - trend = exactly one of: "Bullish", "Bearish", "Neutral"
                - confidenceScore = your overall confidence from 0.05 to 1.0. NEVER emit 0; if signals are weak, use 0.25.
                - summary = 1-3 sentences combining all four reports. Keep it under 200 words.
                - LANGUAGE: All string values (especially `summary` and `trend`) MUST be in English.
                  Do NOT emit Chinese, Mandarin, or any other non-English characters. Prices are in
                  US dollars (USD, $). Do NOT use yuan/元, euro, or any other currency symbol.
                - Output raw JSON only. No ```json fences, no markdown, no extra text.
                """,
            name: "Synthesiser",
            description: "Synthesises analyst reports into a final assessment",
            loggerFactory: loggerFactory);
    }

    public async Task<TradingGraphState> ExecuteAsync(
        TradingGraphState state, CancellationToken cancellationToken)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "AnalystsTeam",
            Message = $"Analyst team starting analysis of {state.Symbol}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        // Pre-flight: confirm the broker actually has bar data for this symbol.
        // If not, skip the (expensive, slow) analyst pipeline and emit a clean
        // low-confidence MarketAnalysis so the graph can terminate the run via
        // the existing minimum-confidence routing rule.
        var preflight = await ComputeMarketMetricsAsync(state.Symbol, cancellationToken);
        var preLastPrice = preflight.LastPrice;
        var preVolumeChange = preflight.VolumeChange;

        // Resolve the canonical company name so we can disambiguate similar
        // tickers in the analyst prompts (e.g. ROK → Rockwell Automation vs
        // ROKU → Roku Inc). Without this the LLM mis-recalls obscure tickers
        // from memory and hallucinates reports about the wrong company.
        var asset = await _alpacaService.GetAssetAsync(state.Symbol, cancellationToken);
        var companyName = asset?.Name;
        var symbolLabel = string.IsNullOrWhiteSpace(companyName)
            ? state.Symbol
            : $"{state.Symbol} ({companyName})";

        if (preLastPrice is null)
        {
            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = NodeId,
                HoundName = "AnalystsTeam",
                Message = $"No market data available for {symbolLabel}; skipping analyst pipeline.",
                Severity = ActivitySeverity.Warning,
            }, cancellationToken);

            var noData = new MarketAnalysis(
                state.Symbol,
                LastPrice: null,
                VolumeChange: null,
                Trend: "Neutral",
                ConfidenceScore: 0d,
                Summary: $"No market data available for {symbolLabel} from the broker. Skipping analysis.",
                CompanyName: companyName);
            return state with { DataOutput = noData };
        }

        // Run each analyst sequentially
        var priceLine = preLastPrice is decimal lp
            ? $"Current price: ${lp:F2} (authoritative — anchor all price levels to this)."
            : "Current price: unavailable.";

        var marketReport = await RunAnalystAsync(_marketAnalyst, "MarketAnalyst",
            $"Analyse the stock {symbolLabel} for the past 7 trading days. Today is {DateTime.UtcNow:yyyy-MM-dd}. {priceLine}",
            state.Symbol, cancellationToken);

        var fundamentalsReport = await RunAnalystAsync(_fundamentalsAnalyst, "FundamentalsAnalyst",
            $"Analyse the fundamentals for {symbolLabel}. {priceLine}",
            state.Symbol, cancellationToken);

        var newsReport = await RunAnalystAsync(_newsAnalyst, "NewsAnalyst",
            $"Analyse recent news and market trends for {symbolLabel}. {priceLine}",
            state.Symbol, cancellationToken);

        var sentimentReport = await RunAnalystAsync(_sentimentAnalyst, "SentimentAnalyst",
            $"Analyse social media sentiment and public opinion for {symbolLabel}. {priceLine}",
            state.Symbol, cancellationToken);

        // Synthesise all reports
        var synthesisPrompt = $"""
            Symbol: {state.Symbol}

            ## Market Report
            {marketReport}

            ## Fundamentals Report
            {fundamentalsReport}

            ## News Report
            {newsReport}

            ## Sentiment Report
            {sentimentReport}

            Now output ONLY the JSON object with symbol, lastPrice, volumeChange, trend, confidenceScore, and summary. No other text.
            """;

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "AnalystsTeam",
            Message = $"All analysts complete for {state.Symbol}, synthesising...",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var synthSession = await _synthesiser.CreateSessionAsync(cancellationToken);
        var synthResponse = await _synthesiser.RunAsync(
            [new ChatMessage(ChatRole.User, synthesisPrompt)],
            synthSession,
            cancellationToken: cancellationToken);

        var json = LlmResponseParser.ExtractJson(synthResponse.Text ?? "{}");
        var analysis = ParseSynthesisJson(json, state.Symbol);

        // Attach individual reports + the pre-flight market metrics (override
        // LLM-supplied lastPrice/volumeChange because the broker numbers are
        // authoritative). ATR(14) and key support/resistance levels are pure
        // deterministic data — never asked of the LLM — so they're attached
        // here too for the downstream strategy hound to select from.
        analysis = analysis with
        {
            LastPrice = preLastPrice ?? analysis.LastPrice,
            VolumeChange = preVolumeChange ?? analysis.VolumeChange,
            Atr14 = preflight.Atr14,
            KeyLevels = preflight.KeyLevels,
            MarketReport = marketReport,
            FundamentalsReport = fundamentalsReport,
            NewsReport = newsReport,
            SentimentReport = sentimentReport,
            CompanyName = companyName,
        };

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "AnalystsTeam",
            Message = $"Analysis complete for {state.Symbol}: {analysis.Trend} (confidence {analysis.ConfidenceScore:P0})",
            Severity = ActivitySeverity.Success,
        }, cancellationToken);

        return state with { DataOutput = analysis };
    }

    private async Task<string> RunAnalystAsync(
        ChatClientAgent analyst, string analystName, string prompt,
        string symbol, CancellationToken cancellationToken)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "AnalystsTeam",
            Message = $"{analystName} analysing {symbol}...",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var session = await analyst.CreateSessionAsync(cancellationToken);
        var response = await analyst.RunAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            session,
            cancellationToken: cancellationToken);

        return response.Text ?? $"No report from {analystName}";
    }

    private static MarketAnalysis ParseSynthesisJson(string json, string symbol)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            decimal? lastPrice = root.TryGetProperty("lastPrice", out var lp) && lp.ValueKind == JsonValueKind.Number
                ? lp.GetDecimal() : null;
            decimal? volumeChange = root.TryGetProperty("volumeChange", out var vc) && vc.ValueKind == JsonValueKind.Number
                ? vc.GetDecimal() : null;
            string trend = root.TryGetProperty("trend", out var tr) && tr.ValueKind == JsonValueKind.String
                ? NormalizeTrend(tr.GetString()) : "Neutral";
            double? confidence = root.TryGetProperty("confidenceScore", out var cs) && cs.ValueKind == JsonValueKind.Number
                ? cs.GetDouble() : (double?)null;
            // Treat a zero confidence as "no signal" rather than "strongly low",
            // otherwise the synthesiser silently aborts the graph whenever the
            // LLM omits or defaults the field.
            if (confidence is 0d) confidence = null;
            string summary = root.TryGetProperty("summary", out var su) && su.ValueKind == JsonValueKind.String
                ? su.GetString() ?? "No summary" : "No summary";

            return new MarketAnalysis(symbol, lastPrice, volumeChange, trend, confidence, summary);
        }
        catch
        {
            return new MarketAnalysis(symbol, null, null, "Neutral", null, json);
        }
    }

    /// <summary>
    /// Collapses the LLM's free-form trend label into one of three canonical
    /// values — <c>"Bullish"</c>, <c>"Bearish"</c>, or <c>"Neutral"</c> — so the
    /// dashboard can render a clean badge regardless of what the model emits.
    /// </summary>
    private static string NormalizeTrend(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Neutral";
        var lower = raw.ToLowerInvariant();
        if (lower.Contains("bull")) return "Bullish";
        if (lower.Contains("bear")) return "Bearish";
        return "Neutral";
    }

    // ── Tool implementations ─────────────────────────────────────────────────

    /// <summary>
    /// Small immutable view over an OHLCV bar so the technical helpers below
    /// can be unit-tested without mocking the full Alpaca <see cref="IBar"/>
    /// surface area. Volume is decimal to mirror the broker SDK.
    /// </summary>
    internal record BarSnapshot(decimal High, decimal Low, decimal Close, decimal Volume, DateTime Time);

    /// <summary>
    /// Deterministically derives last price, relative volume change, ATR(14),
    /// and key support/resistance levels from Alpaca daily bars. Returns an
    /// all-null result if data is unavailable.
    /// </summary>
    private async Task<PreflightMetrics> ComputeMarketMetricsAsync(
        string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var to = DateTime.UtcNow.Date;
            var from = to.AddDays(-45);
            var bars = await _alpacaService.GetBarsAsync(symbol, from, to, BarTimeFrame.Day, cancellationToken);
            if (bars.Count == 0)
                return PreflightMetrics.Empty;

            var ordered = bars
                .OrderBy(b => b.TimeUtc)
                .Select(b => new BarSnapshot(b.High, b.Low, b.Close, b.Volume, b.TimeUtc))
                .ToList();

            var lastBar = ordered[^1];
            decimal lastPrice = lastBar.Close;

            decimal? volumeChange = null;
            if (ordered.Count >= 2)
            {
                var priorBars = ordered.Take(ordered.Count - 1).TakeLast(20).ToList();
                if (priorBars.Count > 0)
                {
                    var avgPrior = priorBars.Average(b => b.Volume);
                    if (avgPrior > 0)
                        volumeChange = Math.Round(lastBar.Volume / avgPrior, 2);
                }
            }

            var atr14 = CalculateAtr14(ordered);
            var keyLevels = CalculateKeyLevels(ordered, lastPrice);

            return new PreflightMetrics(lastPrice, volumeChange, atr14, keyLevels);
        }
        catch
        {
            return PreflightMetrics.Empty;
        }
    }

    /// <summary>
    /// Result of the broker-data pre-flight: deterministic numbers that the
    /// downstream analysts and synthesiser can rely on as authoritative.
    /// </summary>
    internal record PreflightMetrics(
        decimal? LastPrice,
        decimal? VolumeChange,
        decimal? Atr14,
        KeyLevels? KeyLevels)
    {
        public static readonly PreflightMetrics Empty = new(null, null, null, null);
    }

    /// <summary>
    /// Computes 14-period Average True Range using a simple mean of the last
    /// 14 true ranges (close enough to Wilder's smoothing for sizing decisions
    /// and far easier to reason about). Returns <c>null</c> when there are
    /// fewer than 15 bars (14 TR values require a prior close).
    /// </summary>
    internal static decimal? CalculateAtr14(IReadOnlyList<BarSnapshot> bars)
    {
        if (bars.Count < 15) return null;

        var trs = new List<decimal>(bars.Count - 1);
        for (int i = 1; i < bars.Count; i++)
        {
            var prevClose = bars[i - 1].Close;
            var hl = bars[i].High - bars[i].Low;
            var hc = Math.Abs(bars[i].High - prevClose);
            var lc = Math.Abs(bars[i].Low - prevClose);
            trs.Add(Math.Max(hl, Math.Max(hc, lc)));
        }

        var last14 = trs.TakeLast(14).ToList();
        if (last14.Count < 14) return null;
        return Math.Round(last14.Average(), 2);
    }

    /// <summary>
    /// Extracts a concise menu of support/resistance levels from the bar
    /// history. Combines the 20-day high/low with classic prior-day pivot
    /// levels (R1/R2/S1/S2), keeps only values within ±25% of
    /// <paramref name="currentPrice"/>, dedupes near-duplicates (within 0.5%),
    /// and partitions them into support (≤ current) and resistance (≥ current),
    /// both sorted ascending and rounded to 2dp.
    /// </summary>
    internal static KeyLevels? CalculateKeyLevels(IReadOnlyList<BarSnapshot> bars, decimal currentPrice)
    {
        if (bars.Count == 0 || currentPrice <= 0) return null;

        var candidates = new List<decimal>();

        // 20-day range (or whatever's available).
        var window = bars.TakeLast(20).ToList();
        if (window.Count > 0)
        {
            candidates.Add(window.Max(b => b.High));
            candidates.Add(window.Min(b => b.Low));
        }

        // Classic pivot levels derived from the most recent completed bar.
        var pivotBar = bars[^1];
        var pp = (pivotBar.High + pivotBar.Low + pivotBar.Close) / 3m;
        var range = pivotBar.High - pivotBar.Low;
        candidates.Add(2m * pp - pivotBar.Low);   // R1
        candidates.Add(pp + range);                // R2
        candidates.Add(2m * pp - pivotBar.High);  // S1
        candidates.Add(pp - range);                // S2

        // Keep only levels within a ±25% band of current price; outside that
        // a "level" is more likely noise than something a swing trader will
        // act on.
        var lower = currentPrice * 0.75m;
        var upper = currentPrice * 1.25m;
        var filtered = candidates
            .Where(c => c >= lower && c <= upper && c > 0)
            .Select(c => Math.Round(c, 2))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        // Cluster: drop any level within 0.5% of one we've already kept.
        var clusterTolerance = currentPrice * 0.005m;
        var clustered = new List<decimal>();
        foreach (var c in filtered)
        {
            if (clustered.Count == 0 || c - clustered[^1] > clusterTolerance)
                clustered.Add(c);
        }

        var support = clustered.Where(c => c <= currentPrice).ToList();
        var resistance = clustered.Where(c => c >= currentPrice).ToList();

        if (support.Count == 0 && resistance.Count == 0) return null;
        return new KeyLevels(support, resistance);
    }

    private async Task<string> FetchStockDataAsync(
        [Description("Ticker symbol, e.g. AAPL")] string symbol,
        [Description("Start date yyyy-MM-dd")] string startDate,
        [Description("End date yyyy-MM-dd")] string endDate)
    {
        try
        {
            var from = DateTime.TryParse(startDate, out var f) ? f : DateTime.UtcNow.Date.AddDays(-7);
            var to = DateTime.TryParse(endDate, out var t) ? t : DateTime.UtcNow.Date;
            var bars = await _alpacaService.GetBarsAsync(symbol, from, to, BarTimeFrame.Day);

            if (bars.Count == 0)
                return $"NO_DATA: broker returned zero bars for {symbol} between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";

            var summary = bars.Select(b =>
                $"{b.TimeUtc:yyyy-MM-dd}: O={b.Open} H={b.High} L={b.Low} C={b.Close} V={b.Volume}");

            // Prefix with an authoritative banner so the LLM cannot dismiss the
            // data as "future" or "simulated" based on its training cutoff.
            return $"""
                AUTHORITATIVE LIVE BROKER DATA for {symbol} ({from:yyyy-MM-dd} to {to:yyyy-MM-dd}).
                These bars are real and current. Analyse them as-is; do not question their validity.

                {string.Join("\n", summary)}
                """;
        }
        catch (Exception ex)
        {
            return $"ERROR fetching stock data for {symbol}: {ex.Message}";
        }
    }

    private async Task<string> FetchFundamentalsAsync(
        [Description("Ticker symbol")] string symbol)
    {
        try
        {
            var account = await _alpacaService.GetAccountAsync();
            var positions = await _alpacaService.ListPositionsAsync();

            var positionSummary = positions
                .Where(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Select(p => $"  {p.Symbol}: {p.Quantity} shares, market value ${p.MarketValue:F2}, unrealized P&L ${p.UnrealizedProfitLoss:F2}");

            var otherPositions = positions
                .Where(p => !string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Select(p => $"  {p.Symbol}: {p.Quantity} shares, ${p.MarketValue:F2}");

            return $"""
                Account Fundamentals:
                Equity: ${account.Equity:F2}
                Cash: ${account.TradableCash:F2}
                Buying Power: ${account.BuyingPower:F2}

                Current {symbol} position:
                {(positionSummary.Any() ? string.Join("\n", positionSummary) : "  No existing position")}

                Other positions:
                {(otherPositions.Any() ? string.Join("\n", otherPositions) : "  None")}

                Total portfolio positions: {positions.Count}
                """;
        }
        catch (Exception ex)
        {
            return $"ERROR fetching fundamentals for {symbol}: {ex.Message}";
        }
    }

    private async Task<string> FetchNewsAsync(
        [Description("Ticker symbol")] string symbol)
    {
        // Alpaca doesn't provide a news API in the current client.
        // Return a stub that still names the underlying company so the LLM
        // can't drift to a similarly-spelled ticker from its training data.
        var asset = await _alpacaService.GetAssetAsync(symbol);
        var companyLine = asset?.Name is { Length: > 0 } n
            ? $"Underlying company: {n} (ticker {symbol}, exchange {asset!.Exchange})."
            : $"Underlying company: ticker {symbol} (company name not resolved from broker).";

        return $"""
            News data for {symbol} (as of {DateTime.UtcNow:yyyy-MM-dd}):
            {companyLine}
            Note: No external news API is currently configured.
            Base your analysis on general market knowledge of THIS specific company
            (do not substitute a different company with a similar ticker).
            Consider recent earnings seasons, sector trends, and macroeconomic factors
            that may affect {symbol}.
            """;
    }

    private async Task<string> FetchSentimentAsync(
        [Description("Ticker symbol")] string symbol)
    {
        // Stub — no social media API configured yet. Include the canonical
        // company name so the LLM can't confuse similar tickers (e.g. ROK vs
        // ROKU) by pattern-matching against more famous symbols.
        var asset = await _alpacaService.GetAssetAsync(symbol);
        var companyLine = asset?.Name is { Length: > 0 } n
            ? $"Underlying company: {n} (ticker {symbol}, exchange {asset!.Exchange})."
            : $"Underlying company: ticker {symbol} (company name not resolved from broker).";

        return $"""
            Sentiment data for {symbol} (as of {DateTime.UtcNow:yyyy-MM-dd}):
            {companyLine}
            Note: No social media or sentiment API is currently configured.
            Base your sentiment analysis on general market knowledge of THIS
            specific company (do not substitute a similarly-spelled ticker).
            Consider the overall market mood, sector sentiment, and any widely known
            factors affecting {symbol}.
            """;
    }
}
