using Hound.Core.Logging;
using Hound.Core.MarketIntel;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel;

namespace Hound.Trading.Nodes.Analysts;

/// <summary>
/// Sentiment analyst — pulls social media chatter (Bullish/Bearish/Neutral
/// counts and recent messages) and reports on market mood.
/// </summary>
public sealed class SentimentAnalyst : AnalystBase
{
    public override string Name => "SentimentAnalyst";

    private const string PackId = "trading-pack";
    private const string NodeId = "analysts-team-node";
    private const string HoundName = "AnalystsTeam";

    private readonly IAlpacaService _alpacaService;
    private readonly ISentimentService _sentimentService;
    private readonly SentimentSettings _sentimentSettings;

    public SentimentAnalyst(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        ISentimentService sentimentService,
        IOptions<SentimentSettings>? sentimentOptions = null,
        ILoggerFactory? loggerFactory = null)
        : base(activityLogger)
    {
        _alpacaService = alpacaService;
        _sentimentService = sentimentService;
        _sentimentSettings = sentimentOptions?.Value ?? new SentimentSettings();

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                FetchSentimentAsync,
                "get_sentiment",
                "Retrieves social media sentiment and public opinion for a symbol"),
        };

        Configure(new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a social media and sentiment analyst.
                Use the get_sentiment tool to retrieve sentiment data for the company.
                The tool returns Bullish/Bearish/Neutral counts and recent messages.
                Base your analysis on those counts and quoted messages; do not invent
                sentiment data. If the tool returns zero messages, say so plainly.
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
            name: Name,
            description: "Sentiment analyst — social media and public opinion",
            tools: tools,
            loggerFactory: loggerFactory));
    }

    private async Task<string> FetchSentimentAsync(
        [Description("Ticker symbol")] string symbol)
    {
        var asset = await _alpacaService.GetAssetAsync(symbol);
        var companyLine = asset?.Name is { Length: > 0 } n
            ? $"Underlying company: {n} (ticker {symbol}, exchange {asset!.Exchange})."
            : $"Underlying company: ticker {symbol} (company name not resolved from broker).";

        var snapshot = await _sentimentService.GetSentimentAsync(
            symbol, _sentimentSettings.MaxMessages);

        await LogFetchedSentimentAsync(symbol, snapshot);

        if (snapshot.Total == 0)
        {
            return $"""
                Sentiment data for {symbol} (as of {DateTime.UtcNow:yyyy-MM-dd} UTC):
                {companyLine}
                No sentiment messages were returned by configured providers.
                State this clearly in your report rather than inventing sentiment;
                base mood assessment on general market knowledge of THIS specific
                company (do not substitute a similarly-spelled ticker).
                """;
        }

        var sampleSection = snapshot.RecentMessages.Count == 0
            ? string.Empty
            : "\n\nRecent messages:\n" + string.Join("\n",
                snapshot.RecentMessages.Take(5).Select(m => $"- {Truncate(m, 200)}"));

        return $"""
            Sentiment data for {symbol} (as of {DateTime.UtcNow:yyyy-MM-dd} UTC, source {snapshot.Source}):
            {companyLine}
            Counts (last {snapshot.Total} tagged messages): Bullish={snapshot.Bullish}, Bearish={snapshot.Bearish}, Neutral={snapshot.Neutral}.{sampleSection}
            """;
    }

    private async Task LogFetchedSentimentAsync(string symbol, SentimentSnapshot snapshot)
    {
        var metadata = new Dictionary<string, object>
        {
            ["symbol"] = symbol,
            ["source"] = snapshot.Source,
            ["bullish"] = snapshot.Bullish,
            ["bearish"] = snapshot.Bearish,
            ["neutral"] = snapshot.Neutral,
            ["total"] = snapshot.Total,
            ["recentSample"] = snapshot.RecentMessages.Take(5).ToList(),
        };

        await ActivityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = HoundName,
            Message = snapshot.Total > 0
                ? $"SentimentAnalyst fetched {snapshot.Total} message(s) for {symbol} (B{snapshot.Bullish}/Br{snapshot.Bearish}/N{snapshot.Neutral})"
                : $"SentimentAnalyst fetched 0 messages for {symbol}",
            Severity = snapshot.Total > 0 ? ActivitySeverity.Info : ActivitySeverity.Warning,
            Metadata = metadata,
        });
    }
}
