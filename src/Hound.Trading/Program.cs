using Hound.Core.LlmClient;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Hounds;
using Hound.Trading.Services;
using Hound.Trading.Workflows;
using Raven.Client.Documents;
using System.Text.Json;

var builder = Host.CreateApplicationBuilder(args);

// ── Configuration ────────────────────────────────────────────────────────────
builder.Services.Configure<AlpacaSettings>(
    builder.Configuration.GetSection(AlpacaSettings.SectionName));

builder.Services.Configure<TradingWorkflowSettings>(
    builder.Configuration.GetSection(TradingWorkflowSettings.SectionName));

builder.Services.Configure<TunerSettings>(
    builder.Configuration.GetSection(TunerSettings.SectionName));

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
// Forward activity events to the Hound.Api, which persists them and broadcasts
// to SignalR clients for real-time monitoring in the dashboard.
var houndApiUrl = builder.Configuration["HoundApi:BaseUrl"] ?? "http://hound-api:5000";
builder.Services.AddSingleton<IActivityLogger>(sp =>
    new HttpActivityLogger(sp.GetRequiredService<IHttpClientFactory>(), houndApiUrl));

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

// ── TunerHound & TunerHostedService ──────────────────────────────────────────
var tunerModel = builder.Configuration["Hounds:Tuner:Model"] ?? "gemma3";
builder.Services.AddSingleton<TunerHound>(sp =>
{
    var factory = sp.GetRequiredService<IOllamaClientFactory>();
    var chatClient = ((OllamaClientFactory)factory).CreateChatClient(tunerModel);
    var documentStore = sp.GetRequiredService<IDocumentStore>();
    var activityLogger = sp.GetRequiredService<IActivityLogger>();
    var tunerSettings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TunerSettings>>().Value;

    var configDir = tunerSettings.ConfigDirectory
        ?? Path.Combine(AppContext.BaseDirectory, "Config");

    var constraintsPath = Path.Combine(configDir, "TunerConstraints.json");
    var constraints = File.Exists(constraintsPath)
        ? JsonSerializer.Deserialize<TunerConstraints>(
            File.ReadAllText(constraintsPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
          ?? new TunerConstraints()
        : new TunerConstraints();

    return new TunerHound(chatClient, documentStore, activityLogger, factory, configDir, constraints,
        sp.GetService<ILoggerFactory>());
});
builder.Services.AddHostedService<TunerHostedService>();

var host = builder.Build();
host.Run();
