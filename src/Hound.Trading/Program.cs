using Hound.Core.LlmClient;
using Hound.Core.Logging;
using Hound.Core.MarketIntel;
using Hound.Core.Models;
using Hound.Trading;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Hound.Trading.Nodes;
using Hound.Trading.Nodes.Analysts;
using Hound.Trading.Services;
using Hound.Trading.Services.News;
using Microsoft.Extensions.AI;
using Raven.Client.Documents;

var builder = Host.CreateApplicationBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services.Configure<AlpacaSettings>(
    builder.Configuration.GetSection(AlpacaSettings.SectionName));

builder.Services.Configure<TradingGraphSettings>(
    builder.Configuration.GetSection(TradingGraphSettings.SectionName));
builder.Services.Configure<NewsSettings>(
    builder.Configuration.GetSection(NewsSettings.SectionName));

builder.Services.Configure<SentimentSettings>(
    builder.Configuration.GetSection(SentimentSettings.SectionName));
// ── RavenDB ──────────────────────────────────────────────────────────────────
var ravenUrl = builder.Configuration["RavenDb:Url"] ?? "http://ravendb:8080";
builder.Services.AddSingleton<IDocumentStore>(_ =>
{
    var store = new DocumentStore
    {
        Urls = [ravenUrl],
    };
    store.Initialize();
    return store;
});

// ── HTTP / Ollama ─────────────────────────────────────────────────────────────
builder.Services.AddHttpClient();

// ── Activity Logger ───────────────────────────────────────────────────────────
var houndApiUrl = builder.Configuration["HoundApi:BaseUrl"] ?? "http://hound-api:8080";
builder.Services.AddSingleton<IActivityLogger>(sp =>
    new HttpActivityLogger(sp.GetRequiredService<IHttpClientFactory>(), houndApiUrl));

// ── Node Streaming Publisher ─────────────────────────────────────────────────
// Broadcasts live LLM output chunks to the dashboard while nodes are executing.
builder.Services.AddSingleton<HttpNodeStreamPublisher>(sp =>
    new HttpNodeStreamPublisher(
        sp.GetRequiredService<IHttpClientFactory>(),
        houndApiUrl,
        sp.GetService<ILoggerFactory>()?.CreateLogger<HttpNodeStreamPublisher>()));
builder.Services.AddSingleton<INodeStreamPublisher>(sp => sp.GetRequiredService<HttpNodeStreamPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<HttpNodeStreamPublisher>());

var ollamaUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://ollama:11434/v1";
builder.Services.AddSingleton<IOllamaClientFactory>(sp =>
    new OllamaClientFactory(sp.GetRequiredService<IHttpClientFactory>(), ollamaUrl));

// ── Alpaca ────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IAlpacaService, AlpacaService>();
// ── News & Sentiment ───────────────────────────────────────────────────────────
var newsSettingsForHttp = builder.Configuration
    .GetSection(NewsSettings.SectionName).Get<NewsSettings>() ?? new NewsSettings();
builder.Services.AddHttpClient(NewsHttpClients.RssClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, newsSettingsForHttp.HttpTimeoutSeconds));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("HoundAI/1.0 (+https://github.com/mrcarlfarmer/hound-ai)");
});

builder.Services.AddSingleton<INewsProvider, AlpacaNewsProvider>();
builder.Services.AddSingleton<INewsProvider, GoogleNewsRssProvider>();
builder.Services.AddSingleton<INewsProvider, YahooFinanceRssProvider>();
builder.Services.AddSingleton<INewsService>(sp =>
{
    var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsSettings>>().Value;
    var allowed = settings.Providers is { Count: > 0 }
        ? new HashSet<string>(settings.Providers, StringComparer.OrdinalIgnoreCase)
        : null;

    var providers = sp.GetServices<INewsProvider>()
        .Where(p => allowed is null || allowed.Contains(p.Name))
        .ToList();

    return new NewsService(providers, sp.GetService<ILoggerFactory>()?.CreateLogger<NewsService>());
});
builder.Services.AddSingleton<ISentimentService, StockTwitsSentimentService>();
// ── Keyed IChatClient instances ──────────────────────────────────────────────
// StrategyNode uses qwen3:14b; all other nodes use qwen3.5:9b.
var strategyModel = builder.Configuration["Ollama:StrategyModel"] ?? "qwen3:14b";
var defaultModel = builder.Configuration["Ollama:DefaultModel"] ?? "qwen3.5:9b";

builder.Services.AddKeyedSingleton<IChatClient>("strategy", (sp, _) =>
{
    var factory = sp.GetRequiredService<IOllamaClientFactory>();
    return ((OllamaClientFactory)factory).CreateChatClient(strategyModel);
});

builder.Services.AddKeyedSingleton<IChatClient>("default", (sp, _) =>
{
    var factory = sp.GetRequiredService<IOllamaClientFactory>();
    return ((OllamaClientFactory)factory).CreateChatClient(defaultModel);
});

// ── Graph Infrastructure ─────────────────────────────────────────────────────
builder.Services.AddSingleton<IStateStore, RavenStateStore>();
builder.Services.AddSingleton<IResettableExecutor>(sp =>
{
    var ollamaBase = builder.Configuration["Ollama:BaseUrl"] ?? "http://ollama:11434/v1";
    // Strip /v1 suffix for the raw Ollama API
    var rawBase = ollamaBase.Replace("/v1", string.Empty);
    return new OllamaResettableExecutor(
        sp.GetRequiredService<IHttpClientFactory>(), rawBase,
        sp.GetService<ILoggerFactory>()?.CreateLogger<OllamaResettableExecutor>());
});

// ── Analyst Team ──────────────────────────────────────────────────────────────
// Each specialist analyst owns its own ChatClientAgent (prompt + tools); the
// AnalystsTeamNode below just orchestrates them.
builder.Services.AddSingleton<MarketAnalyst>(sp => new MarketAnalyst(
    sp.GetRequiredKeyedService<IChatClient>("default"),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<FundamentalsAnalyst>(sp => new FundamentalsAnalyst(
    sp.GetRequiredKeyedService<IChatClient>("default"),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<NewsAnalyst>(sp => new NewsAnalyst(
    sp.GetRequiredKeyedService<IChatClient>("default"),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetRequiredService<INewsService>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsSettings>>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<SentimentAnalyst>(sp => new SentimentAnalyst(
    sp.GetRequiredKeyedService<IChatClient>("default"),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetRequiredService<ISentimentService>(),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SentimentSettings>>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<AnalystSynthesiser>(sp => new AnalystSynthesiser(
    sp.GetRequiredKeyedService<IChatClient>("default"),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

// ── Nodes ─────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AnalystsTeamNode>(sp => new AnalystsTeamNode(
    sp.GetRequiredService<MarketAnalyst>(),
    sp.GetRequiredService<FundamentalsAnalyst>(),
    sp.GetRequiredService<NewsAnalyst>(),
    sp.GetRequiredService<SentimentAnalyst>(),
    sp.GetRequiredService<AnalystSynthesiser>(),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>()));

builder.Services.AddSingleton<StrategyNode>(sp => new StrategyNode(
    sp.GetRequiredKeyedService<IChatClient>("strategy"),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<RiskNode>(sp => new RiskNode(
    sp.GetRequiredKeyedService<IChatClient>("default"),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<ExecutionNode>(sp => new ExecutionNode(
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetRequiredService<IDocumentStore>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<ApprovalNode>(sp => new ApprovalNode(
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<MonitorNode>(sp =>
{
    var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TradingGraphSettings>>().Value;
    return new MonitorNode(
        sp.GetRequiredService<IAlpacaService>(),
        sp.GetRequiredService<IActivityLogger>(),
        sp.GetRequiredService<IDocumentStore>(),
        sp.GetRequiredService<IResettableExecutor>(),
        settings.MonitorDelaySeconds,
        sp.GetService<ILoggerFactory>());
});

// ── Node dictionary for graph executor ────────────────────────────────────────
builder.Services.AddSingleton<IReadOnlyDictionary<string, INode>>(sp =>
    new Dictionary<string, INode>
    {
        ["analysts-team-node"] = sp.GetRequiredService<AnalystsTeamNode>(),
        ["strategy-node"] = sp.GetRequiredService<StrategyNode>(),
        ["risk-node"] = sp.GetRequiredService<RiskNode>(),
        ["approval-node"] = sp.GetRequiredService<ApprovalNode>(),
        ["execution-node"] = sp.GetRequiredService<ExecutionNode>(),
        ["monitor-node"] = sp.GetRequiredService<MonitorNode>(),
    });

builder.Services.AddSingleton<GraphRunPublisher>(sp =>
    new GraphRunPublisher(
        sp.GetRequiredService<IDocumentStore>(),
        sp.GetRequiredService<IHttpClientFactory>(),
        builder.Configuration["HoundApi:BaseUrl"] ?? "http://hound-api:8080",
        sp.GetService<INodeStreamPublisher>()));
builder.Services.AddSingleton<TradingGraph>();
builder.Services.AddHostedService<TradingWorker>();

var host = builder.Build();
host.Run();
