using System.ClientModel;
using Hound.Core.LlmClient;
using Hound.Core.Logging;
using Hound.Grocery;
using Hound.Grocery.Config;
using Hound.Grocery.Graph;
using Hound.Grocery.Nodes;
using Hound.Grocery.Services;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Embeddings;
using Raven.Client.Documents;

var builder = WebApplication.CreateBuilder(args);

// Optional local secrets (git-ignored). Copy Config/secrets.grocery.example.json
// to Config/secrets.grocery.json for non-docker dev; in docker, .env env vars
// (e.g. Telegram__BotToken, Sainsburys__Username) override these.
builder.Configuration.AddJsonFile("Config/secrets.grocery.json", optional: true, reloadOnChange: false);

// ── Configuration (IOptions<T>) ──────────────────────────────────────────────
builder.Services.Configure<BudgetSettings>(
    builder.Configuration.GetSection(BudgetSettings.SectionName));
builder.Services.Configure<TelegramSettings>(
    builder.Configuration.GetSection(TelegramSettings.SectionName));
builder.Services.Configure<SainsburysSettings>(
    builder.Configuration.GetSection(SainsburysSettings.SectionName));
builder.Services.Configure<ScheduleSettings>(
    builder.Configuration.GetSection(ScheduleSettings.SectionName));
builder.Services.Configure<GroceryOllamaSettings>(
    builder.Configuration.GetSection(GroceryOllamaSettings.SectionName));
builder.Services.Configure<StateFileSettings>(
    builder.Configuration.GetSection(StateFileSettings.SectionName));
builder.Services.Configure<BrowserWorkerSettings>(
    builder.Configuration.GetSection(BrowserWorkerSettings.SectionName));

// ── RavenDB ──────────────────────────────────────────────────────────────────
var ravenUrl = builder.Configuration["RavenDb:Url"] ?? "http://ravendb:8080";
builder.Services.AddSingleton<IDocumentStore>(_ =>
{
    var store = new DocumentStore { Urls = [ravenUrl] };
    store.Initialize();
    return store;
});

// ── HTTP ─────────────────────────────────────────────────────────────────────
builder.Services.AddHttpClient();

// ── Activity Logger ───────────────────────────────────────────────────────────
var houndApiUrl = builder.Configuration["HoundApi:BaseUrl"] ?? "http://hound-api:8080";
builder.Services.AddSingleton<IActivityLogger>(sp =>
    new HttpActivityLogger(sp.GetRequiredService<IHttpClientFactory>(), houndApiUrl));

// ── Ollama / LLM clients ──────────────────────────────────────────────────────
var ollamaUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://ollama:11434/v1";
builder.Services.AddSingleton<IOllamaClientFactory>(sp =>
    new OllamaClientFactory(sp.GetRequiredService<IHttpClientFactory>(), ollamaUrl));

// Keyed IChatClient: both `default` and `vision` point at the unified
// multimodal gemma4:12b so a single model stays resident on the 16 GB card
// (spec §12). The embeddings client uses embeddinggemma.
var defaultModel = builder.Configuration["Ollama:DefaultModel"] ?? "gemma4:12b";
var visionModel = builder.Configuration["Ollama:VisionModel"] ?? "gemma4:12b";
var embeddingModel = builder.Configuration["Ollama:EmbeddingModel"] ?? "embeddinggemma";

builder.Services.AddKeyedSingleton<IChatClient>("default", (sp, _) =>
{
    var factory = (OllamaClientFactory)sp.GetRequiredService<IOllamaClientFactory>();
    return factory.CreateChatClient(defaultModel);
});

builder.Services.AddKeyedSingleton<IChatClient>("vision", (sp, _) =>
{
    var factory = (OllamaClientFactory)sp.GetRequiredService<IOllamaClientFactory>();
    return factory.CreateChatClient(visionModel);
});

builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
{
    var options = new OpenAIClientOptions { Endpoint = new Uri(ollamaUrl) };
    var embeddingClient = new EmbeddingClient(embeddingModel, new ApiKeyCredential("ollama"), options);
    return embeddingClient.AsIEmbeddingGenerator();
});

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<StateFileService>();
builder.Services.AddSingleton<BudgetLedgerService>();
builder.Services.AddSingleton<IBrowserWorkerClient, BrowserWorkerClient>();

// ── Hounds (graph nodes) — Phase 1 placeholder singletons ─────────────────────
builder.Services.AddSingleton<ConciergeHound>(sp => new ConciergeHound(
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<PlannerHound>(sp => new PlannerHound(
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<ShopperHound>(sp => new ShopperHound(
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetRequiredService<IBrowserWorkerClient>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<BudgetHound>(sp => new BudgetHound(
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetRequiredService<BudgetLedgerService>(),
    sp.GetService<ILoggerFactory>()));

builder.Services.AddSingleton<LearnerHound>(sp => new LearnerHound(
    sp.GetRequiredService<IActivityLogger>(),
    sp.GetRequiredService<StateFileService>(),
    sp.GetService<ILoggerFactory>()));

// ── Node dictionary for the (future) graph executor ───────────────────────────
builder.Services.AddSingleton<IReadOnlyDictionary<string, INode>>(sp =>
    new Dictionary<string, INode>
    {
        ["concierge-hound"] = sp.GetRequiredService<ConciergeHound>(),
        ["planner-hound"] = sp.GetRequiredService<PlannerHound>(),
        ["shopper-hound"] = sp.GetRequiredService<ShopperHound>(),
        ["budget-hound"] = sp.GetRequiredService<BudgetHound>(),
        ["learner-hound"] = sp.GetRequiredService<LearnerHound>(),
    });

// ── Hosted worker ──────────────────────────────────────────────────────────────
builder.Services.AddHostedService<GroceryWorker>();

var app = builder.Build();

// Liveness probe for compose health checks (spec §21: "health checks green").
app.MapGet("/health", () => Results.Ok(new { status = "ok", pack = GroceryPack.PackId }));

app.Run();
