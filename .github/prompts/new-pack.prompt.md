---
description: "Scaffold a new pack: project, Dockerfile, Program.cs, graph, nodes, DI, and docker-compose entry"
agent: "agent"
argument-hint: "PackName and purpose, e.g. research-pack for financial research"
---
Create a new pack following all project conventions.

## Inputs
- **Pack name**: {{packName}}
- **Purpose**: {{purpose}}
- **Initial nodes**: {{nodes}}

## Steps

1. **Project** — create `src/Hound.{{packName}}/Hound.{{packName}}.csproj`
   - Target `net9.0`, reference `Hound.Core`
   - Add `Microsoft.Agents.AI`, `Microsoft.Extensions.AI`, `Microsoft.Extensions.Hosting`
   - Add `Raven.Client` if using RavenDB for state

2. **Program.cs** — follow `Hound.Trading/Program.cs` pattern:
   - `Host.CreateApplicationBuilder(args)`
   - Configure `IOptions<T>` for pack settings
   - Register `IDocumentStore` singleton (RavenDB)
   - Register `IActivityLogger` as `HttpActivityLogger`
   - Register `IOllamaClientFactory` + keyed `IChatClient` instances
   - Register each node as singleton
   - Build node dictionary `IReadOnlyDictionary<string, INode>`
   - Register graph executor + `BackgroundService` worker
   - Call `host.Run()`

3. **Graph** — create `Graph/` directory with:
   - `{PackName}Graph.cs` — routing logic via `Route(state)` pattern match
   - `{PackName}GraphState.cs` — immutable record with `with` semantics
   - `INode` implementations in `Nodes/` directory

4. **Config** — create `Config/` with JSON files for each node + `appsettings.json`

5. **Dockerfile** — copy pattern from `src/Hound.Trading/Dockerfile`

6. **Docker Compose** — add service entry to `docker-compose.yml`:
   - Network: `hound-net`
   - Depends on: `ollama`, `ravendb`, `hound-api`
   - Environment: `HoundApi__BaseUrl`, `Ollama__BaseUrl`, `RavenDb__Url`

7. **Pack registration** — add `PackRegistration` document so API discovers the pack

8. **Tests** — create `src/Hound.{{packName}}.Tests/` project with MSTest + Moq

9. **Eval scenarios** — create ≥5 scenarios per node in `src/Hound.Eval/Scenarios/`

10. **Solution** — add both projects to `src/Hound.sln`
