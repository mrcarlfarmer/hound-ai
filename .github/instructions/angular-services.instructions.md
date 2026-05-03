---
description: "Use when editing Angular services or model interfaces that call the Hound API. Maps TypeScript services to REST endpoints and SignalR events to keep client/server in sync."
applyTo: "ui/hound-dashboard/src/app/{services,models}/**"
---
# Angular Service Layer — API Contracts

## Service → Endpoint Mapping

### `ApiService`
| Method | HTTP | Endpoint |
|--------|------|----------|
| `getPacks()` | GET | `/api/packs` |
| `getPack(id)` | GET | `/api/packs/{id}` |
| `getHounds(packId)` | GET | `/api/packs/{packId}/hounds` |
| `getActivity(filters)` | GET | `/api/activity` — query: `pack`, `hound`, `from`, `to`, `page`, `pageSize` |
| `getWatchtowerEvents(page, pageSize)` | GET | `/api/watchtower` |
| `getHealth()` | GET | `/api/health` |

### `TunerService`
| Method | HTTP | Endpoint |
|--------|------|----------|
| `getExperiments(page, pageSize)` | GET | `/api/tuner/experiments` |
| `applyExperiment(id)` | POST | `/api/tuner/experiments/{id}/apply` |
| `rejectExperiment(id)` | POST | `/api/tuner/experiments/{id}/reject` |

### `SignalrService`
| Method/Event | Direction | Hub method/event |
|--------------|-----------|-----------------|
| `subscribeToPack(packId)` | client → hub | `SubscribeToPack` |
| `unsubscribeFromPack(packId)` | client → hub | `UnsubscribeFromPack` |
| `onActivity$` | hub → client | `OnActivity` |
| `onWatchtowerEvent$` | hub → client | `OnWatchtowerEvent` |

Hub URL: `http://localhost:5000/hubs/activity`

## Model Sync Rules

TypeScript interfaces in `models/index.ts` must mirror the C# models in `Hound.Core/Models/`:

| TypeScript | C# | Notes |
|-----------|-----|-------|
| `Pack` | `Pack` | `status` is string union, `lastActivity` is ISO string |
| `HoundInfo` | `HoundInfo` | — |
| `ActivityLog` | `ActivityLog` | `metadata` is `Record<string, unknown>` |
| `TunerExperiment` | `TunerExperiment` | `status` uses kebab-case values (`pending-review`) |
| `WatchtowerEvent` | `WatchtowerEvent` | — |
| `HealthReport` | `HealthReport` | nested `ServiceHealth[]` |
| `PagedResult<T>` | (server shape) | `items`, `totalCount`, `page`, `pageSize` |

- All property names are **camelCase** (API serializes with camelCase)
- Dates arrive as ISO 8601 strings — keep as `string`, format in templates
- Enum-like fields use string union types, not TypeScript `enum`

## Conventions

- Base URL: `http://localhost:5000` — defined as `private readonly baseUrl` in each service
- Return `Observable<T>` — components subscribe; no `.toPromise()` or `firstValueFrom` in services
- Use `HttpParams` for query strings — never concatenate query params manually
- SignalR events exposed as `Observable` via RxJS `Subject` (not `EventEmitter`)
- `SIGNALR_CONNECTION_FACTORY` injection token enables test mocking without importing the real hub

## When Adding a New Endpoint

1. Add method to the appropriate service (`ApiService` or create a new one for a new domain)
2. Add/update TypeScript interface in `models/index.ts` to match the C# response model
3. Ensure property names match API's camelCase serialization
4. Add the corresponding test in the service's `.spec.ts` file
