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

## DI Registration (Program.cs)
- Each hound: `AddSingleton<THound>(sp => { ... })` with factory lambda
- Factory resolves `IOllamaClientFactory`, casts to `OllamaClientFactory`, calls `CreateChatClient(model)`
- Model name from `builder.Configuration["Hounds:{HoundName}:Model"]`
