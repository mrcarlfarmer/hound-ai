---
description: "Use when editing API controllers, SignalR hubs, repositories, or services in Hound.Api. Documents REST endpoints, hub events, response models, and conventions."
applyTo: "src/Hound.Api/**"
---
# Hound AI — API Contracts

## REST Endpoints

### `/api/activity`
| Verb | Route | Parameters | Returns |
|------|-------|-----------|---------|
| GET | `/api/activity` | Query: `pack?`, `hound?`, `from?`, `to?`, `page=1`, `pageSize=50` | `IEnumerable<ActivityLog>` |
| POST | `/api/activity` | Body: `ActivityLog` | `200` + broadcasts `OnActivity` to SignalR group |

### `/api/health`
| Verb | Route | Returns |
|------|-------|---------|
| GET | `/api/health` | `HealthReport` (overall status + per-service `ServiceHealth` list) |

### `/api/packs`
| Verb | Route | Returns |
|------|-------|---------|
| GET | `/api/packs` | `IEnumerable<Pack>` |
| GET | `/api/packs/{id}` | `Pack` or `404` |
| GET | `/api/packs/{packId}/hounds` | `IEnumerable<HoundInfo>` |

### `/api/tuner`
| Verb | Route | Returns |
|------|-------|---------|
| GET | `/api/tuner/experiments` | Paginated `IEnumerable<TunerExperiment>` (`page`, `pageSize`) |
| GET | `/api/tuner/experiments/{id}` | `TunerExperiment` or `404` |
| POST | `/api/tuner/experiments/{id}/apply` | `200`/`409`/`422`/`500` — applies candidate config to disk |
| POST | `/api/tuner/experiments/{id}/reject` | `200` or `404` |
| POST | `/api/tuner/pause` | `{ message, isPaused: true }` |
| POST | `/api/tuner/resume` | `{ message, isPaused: false }` |

Valid hound names for apply: `StrategyHound`, `RiskHound`, `AnalysisHound`, `ExecutionHound`.

### `/api/watchtower`
| Verb | Route | Returns |
|------|-------|---------|
| GET | `/api/watchtower` | Paginated `IEnumerable<WatchtowerEvent>` |
| POST | `/api/watchtower/webhook` | `200` — parses shoutrrr payload, stores event, broadcasts `OnWatchtowerEvent` |

## SignalR Hub — `/hubs/activity`

### Server methods (client → hub)
- `SubscribeToPack(string packId)` — join group `pack-{packId}`
- `UnsubscribeFromPack(string packId)` — leave group `pack-{packId}`
- `PublishActivity(ActivityLog activity)` — relay to pack subscribers

### Client events (hub → client)
- `OnActivity(ActivityLog)` — sent to group `pack-{packId}`
- `OnWatchtowerEvent(WatchtowerEvent)` — sent to all clients

## Key Models

| Model | Key Fields |
|-------|-----------|
| `ActivityLog` | `Id`, `PackId`, `HoundId`, `HoundName`, `Message`, `Severity` (Info/Warning/Error/Success), `Timestamp`, `Metadata?` |
| `Pack` | `Id`, `Name`, `Status` (Idle/Running/Error/Stopped), `HoundCount`, `LastActivity?`, `HoundIds` |
| `HoundInfo` | `Id`, `Name`, `PackId`, `Status` (Idle/Processing/Error/Disabled), `LastActivity?` |
| `TunerExperiment` | `Id`, `HoundName`, `ConfigBefore`, `ConfigAfter`, `BaselineScore`, `CandidateScore`, `Delta`, `Status`, `Rationale` |
| `WatchtowerEvent` | `Id`, `ContainerName`, `ImageName`, `OldImageId`, `NewImageId`, `Action`, `Timestamp`, `RawPayload` |

## Conventions

- All controllers: `[ApiController]`, `[Route("api/[controller]")]`, `CancellationToken` on every async method
- JSON serialization: **camelCase** (both REST and SignalR)
- CORS origin: `http://localhost:4200`
- Repositories are **scoped**; `TunerStateService` is **singleton**
- RavenDB indexes: `ActivityLog_ByPackAndTime`, `ActivityLog_ByHoundAndTime`
- POST endpoints that modify state should broadcast relevant SignalR events
- Return `404 NotFound` for missing resources, never null bodies
