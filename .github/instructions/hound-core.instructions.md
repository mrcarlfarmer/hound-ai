---
description: Use when editing core interfaces, logging, LLM client factory, or shared model types. Covers IActivityLogger, IOllamaClientFactory, config models, and the activity flow.
applyTo: src/Hound.Core/**
---

# Hound.Core — Shared Infrastructure

## Module Layout

```
Hound.Core/
  Logging/     — IActivityLogger, HttpActivityLogger
  LlmClient/   — IOllamaClientFactory, OllamaClientFactory
  Models/       — All shared document/config types
```

## IActivityLogger

Central logging interface used by every node and graph component.

```csharp
Task LogActivityAsync(ActivityLog activity, CancellationToken cancellationToken = default);
Task<IReadOnlyList<ActivityLog>> GetActivitiesAsync(...);
```

- **`HttpActivityLogger`** — Production impl; POSTs to `hound-api` REST endpoint
- **`NullActivityLogger`** — Used by eval harness (in `Hound.Eval`)
- Always set `PackId`, `HoundId`, `HoundName`, `Message`, `Severity`
- Use `Metadata` dictionary for structured data (runId, node, phase, etc.)

## IOllamaClientFactory

Creates HTTP clients pointed at the Ollama OpenAI-compatible endpoint (`/v1`).

- `CreateClient(modelName)` → raw `HttpClient` for direct API calls
- `OllamaClientFactory.CreateChatClient(model)` → `IChatClient` for use with `ChatClientAgent`
- Registered as singleton; base URL from config `Ollama:BaseUrl`

## Configuration Models (`Models/HoundConfigs.cs`)

All config classes inherit `BaseHoundConfig`:
```csharp
public class BaseHoundConfig
{
    public string Model { get; set; }
    public string Instructions { get; set; }
    public int MaxTokens { get; set; }
    public double Temperature { get; set; }
}
```

Specialized configs add domain-specific properties (thresholds, weights, limits). Bound via `IOptions<T>` from `Config/*.json` in each pack.

## Document Models

| Model | Purpose | Database |
|-------|---------|----------|
| `ActivityLog` | Hound activity entries | `HoundAI` |
| `GraphRun` | Graph execution snapshots | `hound-trading-pack` |
| `TradeDocument` | Trade lifecycle records | `hound-trading-pack` |
| `RunRequest` | On-demand graph run requests | `hound-trading-pack` |
| `TunerExperiment` | Auto-tuning experiment results | `hound-trading-pack` |
| `Pack` / `PackRegistration` | Pack metadata for API | `HoundAI` |
| `HoundInfo` / `HoundMessage` | Hound metadata and messages | `HoundAI` |
| `ServiceHealth` | Health check records | `HoundAI` |

## Activity Flow

```
Node → IActivityLogger.LogActivityAsync()
     → HttpActivityLogger POSTs to hound-api/api/activity
     → RavenActivityService stores in RavenDB "HoundAI" database
     → SignalR hub broadcasts to connected dashboards
```

## Adding a New Model

1. Add record/class to `Models/`
2. If it needs an index, create in `Hound.Api/Indexes/`
3. If exposed via API, add a repository in `Hound.Api/Repositories/`
4. If shown in dashboard, add corresponding TypeScript interface in `ui/.../models/`
