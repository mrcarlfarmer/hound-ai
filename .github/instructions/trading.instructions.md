---
description: "Use when editing trading pack hounds, workflows, services, or config. Covers the hound pipeline, TunerHound autoresearch, Alpaca integration, and settings patterns."
applyTo: "src/Hound.Trading/**"
---
# Trading Pack Conventions

## Hound Pipeline
Sequential workflow: `AnalysisHound` → `StrategyHound` → `RiskHound` → `ExecutionHound`
- Each hound returns a typed record (`MarketAnalysis`, `TradingDecision`, `RiskAssessment`, `ExecutionResult`) defined in `Hounds/HoundModels.cs`
- Downstream hounds receive upstream records as context — records flow through `TradingWorkflow.RunAsync()`
- Confidence thresholds and symbol lists are configurable via `TradingWorkflowSettings`

## TunerHound (Autoresearch)
- Runs on a timer via `TunerHostedService` (`IHostedService`), not in the main workflow
- Round-robin selects a hound, proposes config changes via LLM, scores via heuristic `ScoreConfig()`
- Experiments stored in RavenDB (`TunerExperiment` documents) for human review via the API
- Config snapshot inner types per hound (e.g., `StrategyConfigSnapshot`) for deserialization in scoring
- Pause/resume controlled via `TunerStateService` (singleton) and API endpoints

## Settings Classes
- `TradingWorkflowSettings` — co-located in `Workflows/TradingWorkflow.cs`
- `TunerSettings` — co-located in `Services/TunerHostedService.cs`
- `AlpacaSettings` — standalone in `AlpacaClient/AlpacaSettings.cs`
- All bound via `IOptions<T>` with section names matching JSON config keys

## Alpaca Integration
- `IAlpacaService` interface in `AlpacaClient/` — wraps paper trading API
- Hound tools (`fetch_market_data`, `get_portfolio`, `place_market_order`) delegate to `IAlpacaService`
- Credentials via `Alpaca:ApiKeyId` / `Alpaca:SecretKey` in config or env vars (`Alpaca__ApiKeyId`)
- `IAlpacaService.ListNewsAsync(symbols, since, maxItems, ct)` wraps `IAlpacaDataClient.ListNewsArticlesAsync`; consumed by `AlpacaNewsProvider`

## Market Intel Services
- `INewsService` and `ISentimentService` (with `INewsProvider`, `NewsArticle`, `SentimentSnapshot`) live in `Hound.Core/MarketIntel/` so other packs can consume them without referencing `Hound.Trading`
- `NewsService` aggregator (also in `Hound.Core/MarketIntel/`) fans out to all registered `INewsProvider`s in parallel, dedupes by normalised headline, returns top-N by `PublishedAt`
- Provider implementations live in `Hound.Trading/Services/News/`: `AlpacaNewsProvider` (`Name="alpaca"`), `GoogleNewsRssProvider` (`Name="googlenews"`), `YahooFinanceRssProvider` (`Name="yahoofinance"`)
- `ISentimentService` → `StockTwitsSentimentService` — counts bullish/bearish/neutral messages and captures recent message bodies
- RSS providers share the named `HttpClient` `NewsHttpClients.RssClientName` (`"news-rss"`) registered with a configured timeout + User-Agent
- `NewsSettings` (section `"News"`) and `SentimentSettings` (section `"Sentiment"`) live in `Hound.Core/Models/MarketIntelSettings.cs`. The `Providers: [...]` allow-list (case-insensitive) selects which registered providers to use; an empty/missing list means "all registered providers"
- `AnalystsTeamNode` injects both services and logs raw fetched items via `IActivityLogger` with `Metadata` (keys: `symbol`, `articleCount`, `lookbackHours`, `countsBySource`, `articles[]`) before formatting Markdown for the LLM
- LLM tool instructions explicitly require citing only headlines returned by the tool; if zero articles, the analyst must say so plainly rather than fabricate

## DI Registration (Program.cs)
- Each hound: `AddSingleton<THound>(sp => { ... })` with factory lambda
- Factory resolves `IOllamaClientFactory`, casts to `OllamaClientFactory`, calls `CreateChatClient(model)`
- Model name from `builder.Configuration["Hounds:{HoundName}:Model"]`
