using Hound.Core.LlmClient;
using Hound.Core.Logging;
using Hound.Trading;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Hound.Trading.Nodes;
using Microsoft.Extensions.AI;
using Raven.Client.Documents;

var builder = Host.CreateApplicationBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services.Configure<AlpacaSettings>(
    builder.Configuration.GetSection(AlpacaSettings.SectionName));

builder.Services.Configure<TradingGraphSettings>(
    builder.Configuration.GetSection(TradingGraphSettings.SectionName));

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

var ollamaUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://ollama:11434/v1";
builder.Services.AddSingleton<IOllamaClientFactory>(sp =>
    new OllamaClientFactory(sp.GetRequiredService<IHttpClientFactory>(), ollamaUrl));

// ── Alpaca ────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IAlpacaService, AlpacaService>();

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

// ── Nodes ─────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AnalystsTeamNode>(sp => new AnalystsTeamNode(
    sp.GetRequiredKeyedService<IChatClient>("default"),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<StrategyNode>(sp => new StrategyNode(
    sp.GetRequiredKeyedService<IChatClient>("strategy"),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<RiskNode>(sp => new RiskNode(
    sp.GetRequiredKeyedService<IChatClient>("default"),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<ExecutionNode>(sp => new ExecutionNode(
    sp.GetRequiredKeyedService<IChatClient>("default"),
    sp.GetRequiredService<IAlpacaService>(),
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetRequiredService<IDocumentStore>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<MonitorNode>(sp =>
{
    var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TradingGraphSettings>>().Value;
    return new MonitorNode(
        sp.GetRequiredKeyedService<IChatClient>("default"),
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
        ["execution-node"] = sp.GetRequiredService<ExecutionNode>(),
        ["monitor-node"] = sp.GetRequiredService<MonitorNode>(),
    });

builder.Services.AddSingleton<GraphRunPublisher>(sp =>
    new GraphRunPublisher(
        sp.GetRequiredService<IDocumentStore>(),
        sp.GetRequiredService<IHttpClientFactory>(),
        builder.Configuration["HoundApi:BaseUrl"] ?? "http://hound-api:8080"));
builder.Services.AddSingleton<TradingGraph>();
builder.Services.AddHostedService<TradingWorker>();

var host = builder.Build();
host.Run();
