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

## Keyed IChatClient Models
Three keyed `IChatClient` instances are registered in `Program.cs`, each backed by a
model name from the `Ollama` config section (overridable via `Ollama__*` env vars):
- `"strategy"` — `Ollama:StrategyModel` (default `qwen3:14b`). Used by the StrategyNode
  **coordinator** that emits the final `TradingDecision` JSON.
- `"debate"` — `Ollama:DebateModel` (default `qwen3.5:9b`). Used by the StrategyNode
  **bull/bear debaters** so the multi-turn debate runs on a smaller, faster model than
  the coordinator, bounding debate wall-clock and GPU time. `StrategyNode` falls back to
  its coordinator client when no debate client is supplied (e.g. the eval harness).
- `"default"` — `Ollama:DefaultModel` (default `qwen3.5:9b`). Used by the analyst team,
  RiskNode, and other nodes.

Chosen configuration (issue #40): debaters run on `qwen3.5:9b` while the coordinator stays
on `qwen3:14b`. `qwen3.5:9b` is already pulled by `infra/ollama/pull-models.sh`; point
`Ollama:DebateModel` at a different local model (e.g. `qwen3:4b`) to trial a smaller
debate model, adding it to `pull-models.sh` first.
