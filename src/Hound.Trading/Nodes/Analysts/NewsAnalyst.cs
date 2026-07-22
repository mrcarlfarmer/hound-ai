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
/// News analyst — pulls recent headlines from configured news providers and
/// reports on their likely market impact.
/// </summary>
public sealed class NewsAnalyst : AnalystBase
{
    public override string Name => "NewsAnalyst";

    private const string PackId = "trading-pack";
    private const string NodeId = "analysts-team-node";
    private const string HoundName = "AnalystsTeam";

    private readonly IAlpacaService _alpacaService;
    private readonly INewsService _newsService;
    private readonly NewsSettings _newsSettings;

    public NewsAnalyst(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        INewsService newsService,
        IOptions<NewsSettings>? newsOptions = null,
        ILoggerFactory? loggerFactory = null)
        : base(activityLogger)
    {
        _alpacaService = alpacaService;
        _newsService = newsService;
        _newsSettings = newsOptions?.Value ?? new NewsSettings();

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                FetchNewsAsync,
                "get_news",
                "Retrieves recent news and market events for a symbol"),
        };

        Configure(new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a news analyst tasked with analysing recent news and market trends.
                Use the get_news tool to retrieve recent news for the company.
                Cite headlines returned by the tool directly — do NOT invent additional
                stories or paraphrase headlines into fabricated facts.
                If the tool reports zero articles, say so plainly and rely on general
                market knowledge of the company; do not pretend you have current news.
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
            name: Name,
            description: "News analyst — macro and company-specific news impact",
            tools: tools,
            loggerFactory: loggerFactory));
    }

    private async Task<string> FetchNewsAsync(
        [Description("Ticker symbol")] string symbol)
    {
        var asset = await _alpacaService.GetAssetAsync(symbol);
        var companyLine = asset?.Name is { Length: > 0 } n
            ? $"Underlying company: {n} (ticker {symbol}, exchange {asset!.Exchange})."
            : $"Underlying company: ticker {symbol} (company name not resolved from broker).";

        var lookback = TimeSpan.FromHours(Math.Max(1, _newsSettings.LookbackHours));
        var articles = await _newsService.GetRecentNewsAsync(
            symbol, _newsSettings.MaxItems, lookback);

        await LogFetchedNewsAsync(symbol, articles, lookback);

        if (articles.Count == 0)
        {
            return $"""
                News data for {symbol} (as of {DateTime.UtcNow:yyyy-MM-dd} UTC):
                {companyLine}
                No articles were returned by configured news providers in the
                last {(int)lookback.TotalHours} hours. State this clearly in
                your report rather than inventing news; base impact assessment
                on general market knowledge of THIS specific company (do not
                substitute a similarly-spelled ticker).
                """;
        }

        var lines = articles.Select((a, i) => FormatArticle(i + 1, a));
        return $"""
            News data for {symbol} (as of {DateTime.UtcNow:yyyy-MM-dd} UTC):
            {companyLine}
            {articles.Count} article(s) from the last {(int)lookback.TotalHours} hours,
            ordered most recent first. Cite these headlines directly in your report —
            do NOT invent additional items.

            {string.Join("\n\n", lines)}
            """;
    }

    private static string FormatArticle(int index, NewsArticle article)
    {
        var summary = string.IsNullOrWhiteSpace(article.Summary)
            ? string.Empty
            : $"\n   {Truncate(article.Summary, 280)}";
        var url = string.IsNullOrWhiteSpace(article.Url)
            ? string.Empty
            : $"\n   {article.Url}";
        return $"{index}. [{article.Source}] {article.Headline} ({article.PublishedAt:yyyy-MM-dd HH:mm} UTC){summary}{url}";
    }

    private async Task LogFetchedNewsAsync(
        string symbol, IReadOnlyList<NewsArticle> articles, TimeSpan lookback)
    {
        var bySource = articles
            .GroupBy(a => a.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (object)g.Count(), StringComparer.OrdinalIgnoreCase);

        var compact = articles.Select(a => (object)new Dictionary<string, object?>
        {
            ["source"] = a.Source,
            ["headline"] = a.Headline,
            ["url"] = a.Url,
            ["publishedAt"] = a.PublishedAt.ToString("O"),
        }).ToList();

        var metadata = new Dictionary<string, object>
        {
            ["symbol"] = symbol,
            ["articleCount"] = articles.Count,
            ["lookbackHours"] = (int)lookback.TotalHours,
            ["countsBySource"] = bySource,
            ["articles"] = compact,
        };

        await ActivityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = HoundName,
            Message = articles.Count > 0
                ? $"NewsAnalyst fetched {articles.Count} article(s) for {symbol}"
                : $"NewsAnalyst fetched 0 articles for {symbol} (last {(int)lookback.TotalHours}h)",
            Severity = articles.Count > 0 ? ActivitySeverity.Info : ActivitySeverity.Warning,
            Metadata = metadata,
        });
    }
}
