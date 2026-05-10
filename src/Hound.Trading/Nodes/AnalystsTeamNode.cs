using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
        var marketTools = new List<AITool>
        {
            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Ticker symbol, e.g. AAPL")] string symbol,
                 [System.ComponentModel.Description("Start date yyyy-MM-dd")] string startDate,
                 [System.ComponentModel.Description("End date yyyy-MM-dd")] string endDate) =>
                    FetchStockDataAsync(symbol, startDate, endDate),
                "get_stock_data",
                "Retrieves OHLCV price bars for a symbol in a date range"),
        };

        _marketAnalyst = new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a trading assistant tasked with analysing financial markets.
                Use the get_stock_data tool to retrieve recent OHLCV price bars.
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
                ([System.ComponentModel.Description("Ticker symbol")] string symbol) =>
                    FetchFundamentalsAsync(symbol),
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
                ([System.ComponentModel.Description("Ticker symbol")] string symbol) =>
                    FetchNewsAsync(symbol),
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
                ([System.ComponentModel.Description("Ticker symbol")] string symbol) =>
                    FetchSentimentAsync(symbol),
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
                - confidenceScore = your overall confidence from 0.0 to 1.0
                - summary = 1-3 sentences combining all four reports. Keep it under 200 words.
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

        // Run each analyst sequentially
        var marketReport = await RunAnalystAsync(_marketAnalyst, "MarketAnalyst",
            $"Analyse the stock {state.Symbol} for the past 7 trading days. Today is {DateTime.UtcNow:yyyy-MM-dd}.",
            state.Symbol, cancellationToken);

        var fundamentalsReport = await RunAnalystAsync(_fundamentalsAnalyst, "FundamentalsAnalyst",
            $"Analyse the fundamentals for {state.Symbol}.",
            state.Symbol, cancellationToken);

        var newsReport = await RunAnalystAsync(_newsAnalyst, "NewsAnalyst",
            $"Analyse recent news and market trends for {state.Symbol}.",
            state.Symbol, cancellationToken);

        var sentimentReport = await RunAnalystAsync(_sentimentAnalyst, "SentimentAnalyst",
            $"Analyse social media sentiment and public opinion for {state.Symbol}.",
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

        // Attach individual reports
        analysis = analysis with
        {
            MarketReport = marketReport,
            FundamentalsReport = fundamentalsReport,
            NewsReport = newsReport,
            SentimentReport = sentimentReport,
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
                ? tr.GetString() ?? "Unknown" : "Unknown";
            double? confidence = root.TryGetProperty("confidenceScore", out var cs) && cs.ValueKind == JsonValueKind.Number
                ? cs.GetDouble() : null;
            string summary = root.TryGetProperty("summary", out var su) && su.ValueKind == JsonValueKind.String
                ? su.GetString() ?? "No summary" : "No summary";

            return new MarketAnalysis(symbol, lastPrice, volumeChange, trend, confidence, summary);
        }
        catch
        {
            return new MarketAnalysis(symbol, null, null, "Unknown", null, json);
        }
    }

    // ── Tool implementations ─────────────────────────────────────────────────

    private async Task<string> FetchStockDataAsync(string symbol, string startDate, string endDate)
    {
        try
        {
            var from = DateTime.TryParse(startDate, out var f) ? f : DateTime.UtcNow.Date.AddDays(-7);
            var to = DateTime.TryParse(endDate, out var t) ? t : DateTime.UtcNow.Date;
            var bars = await _alpacaService.GetBarsAsync(symbol, from, to, BarTimeFrame.Day);

            if (bars.Count == 0)
                return $"No bar data found for {symbol} between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";

            var summary = bars.Select(b =>
                $"{b.TimeUtc:yyyy-MM-dd}: O={b.Open} H={b.High} L={b.Low} C={b.Close} V={b.Volume}");

            return $"OHLCV data for {symbol}:\n{string.Join("\n", summary)}";
        }
        catch (Exception ex)
        {
            return $"ERROR fetching stock data for {symbol}: {ex.Message}";
        }
    }

    private async Task<string> FetchFundamentalsAsync(string symbol)
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

    private Task<string> FetchNewsAsync(string symbol)
    {
        // Alpaca doesn't provide a news API in the current client.
        // Return a stub indicating no external news source is configured.
        var result = $"""
            News data for {symbol} (as of {DateTime.UtcNow:yyyy-MM-dd}):
            Note: No external news API is currently configured.
            Please base your analysis on general market knowledge and the symbol context.
            Consider recent earnings seasons, sector trends, and macroeconomic factors
            that may affect {symbol}.
            """;
        return Task.FromResult(result);
    }

    private Task<string> FetchSentimentAsync(string symbol)
    {
        // Stub — no social media API configured yet.
        var result = $"""
            Sentiment data for {symbol} (as of {DateTime.UtcNow:yyyy-MM-dd}):
            Note: No social media or sentiment API is currently configured.
            Please base your sentiment analysis on general market knowledge.
            Consider the overall market mood, sector sentiment, and any widely known
            factors affecting {symbol}.
            """;
        return Task.FromResult(result);
    }
}
