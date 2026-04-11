using Hound.Core.LlmClient;
using Hound.Core.Logging;
using Hound.Trading;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Hounds;
using Hound.Trading.Workflows;
using Raven.Client.Documents;

var builder = Host.CreateApplicationBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services.Configure<AlpacaSettings>(
    builder.Configuration.GetSection(AlpacaSettings.SectionName));

builder.Services.Configure<TradingWorkflowSettings>(
    builder.Configuration.GetSection(TradingWorkflowSettings.SectionName));

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

builder.Services.AddSingleton<IActivityLogger, RavenActivityLogger>();

// ── HTTP / Ollama ─────────────────────────────────────────────────────────────
builder.Services.AddHttpClient();

var ollamaUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://ollama:11434/v1";
builder.Services.AddSingleton<IOllamaClientFactory>(sp =>
    new OllamaClientFactory(sp.GetRequiredService<IHttpClientFactory>(), ollamaUrl));

// ── Alpaca ────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IAlpacaService, AlpacaService>();

// ── Hounds ────────────────────────────────────────────────────────────────────
var analysisModel = builder.Configuration["Hounds:Analysis:Model"] ?? "gemma3";
var strategyModel = builder.Configuration["Hounds:Strategy:Model"] ?? "gemma3";
var riskModel = builder.Configuration["Hounds:Risk:Model"] ?? "gemma3";
var executionModel = builder.Configuration["Hounds:Execution:Model"] ?? "gemma3";

builder.Services.AddSingleton<AnalysisHound>(sp =>
{
    var factory = sp.GetRequiredService<IOllamaClientFactory>();
    var chatClient = ((OllamaClientFactory)factory).CreateChatClient(analysisModel);
    return new AnalysisHound(chatClient, sp.GetRequiredService<IAlpacaService>(),
        sp.GetRequiredService<IActivityLogger>(), sp.GetService<ILoggerFactory>());
});

builder.Services.AddSingleton<StrategyHound>(sp =>
{
    var factory = sp.GetRequiredService<IOllamaClientFactory>();
    var chatClient = ((OllamaClientFactory)factory).CreateChatClient(strategyModel);
    return new StrategyHound(chatClient, sp.GetRequiredService<IActivityLogger>(),
        sp.GetService<ILoggerFactory>());
});

builder.Services.AddSingleton<RiskHound>(sp =>
{
    var factory = sp.GetRequiredService<IOllamaClientFactory>();
    var chatClient = ((OllamaClientFactory)factory).CreateChatClient(riskModel);
    return new RiskHound(chatClient, sp.GetRequiredService<IAlpacaService>(),
        sp.GetRequiredService<IActivityLogger>(), sp.GetService<ILoggerFactory>());
});

builder.Services.AddSingleton<ExecutionHound>(sp =>
{
    var factory = sp.GetRequiredService<IOllamaClientFactory>();
    var chatClient = ((OllamaClientFactory)factory).CreateChatClient(executionModel);
    return new ExecutionHound(chatClient, sp.GetRequiredService<IAlpacaService>(),
        sp.GetRequiredService<IActivityLogger>(), sp.GetService<ILoggerFactory>());
});

// ── Workflow & Worker ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<TradingWorkflow>();
builder.Services.AddHostedService<TradingWorker>();

var host = builder.Build();
host.Run();
