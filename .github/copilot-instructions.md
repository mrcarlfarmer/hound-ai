# Hound AI — Copilot Instructions

## Project Overview
Hound AI is a containerized multi-agent platform running locally in WSL2 via Docker Compose. It uses Microsoft Agent Framework (.NET) for agent orchestration, Ollama for local LLMs, RavenDB for activity logging, and an Angular + ASP.NET Core monitoring dashboard.

## Naming Conventions
- **Agents** → **"Hounds"**
- **Agent groups** → **"Packs"**
- Use these terms consistently in code, comments, logs, and documentation

## Tech Stack
- .NET 9 / ASP.NET Core, Microsoft Agent Framework v1.1.0 (`Microsoft.Agents.AI`)
- Angular 21 (standalone components, SCSS), `@microsoft/signalr`
- RavenDB (activity logging), Ollama (local LLMs), Alpaca Markets (paper trading)
- Docker Compose on WSL2, nginx reverse proxy for UI
- Testing: MSTest + Moq (.NET), vitest + jsdom (Angular)

## Build and Test
```bash
# .NET — restore, build, test
dotnet restore src/Hound.sln
dotnet build src/Hound.sln --no-restore
dotnet test src/Hound.sln --no-build --no-restore

# Angular — install and test
cd ui/hound-dashboard && npm ci
npx ng test --watch=false

# Eval harness — validate scenarios (no LLM needed)
dotnet run --project src/Hound.Eval -- --dry-run

# Docker — full stack
docker compose -f docker-compose.yml -f docker-compose.dev.yml up
```

## Architecture
7 Docker containers on a `hound-net` bridge network:
- `ollama` — Local LLM server (GPU passthrough, port 11434)
- `ollama-init` — One-shot init container that pulls models via `infra/ollama/pull-models.sh`
- `ravendb` — Document DB for activity logging (port 8080)
- `trading-pack` — Trading hounds: Analysis → Strategy → Risk → Execution (+ Tuner)
- `hound-api` — ASP.NET Core API + SignalR hub (port 5000, internal 8080)
- `hound-ui` — Angular SPA via nginx (port 4200)
- `watchtower` — GitOps auto-deploy from GHCR

## Key Patterns
- **One container per pack** — all hounds in a pack share a process
- **Graph-based orchestration** — cyclic state machine with `INode` pipeline; see [graph-workflows.instructions.md](.github/instructions/graph-workflows.instructions.md)
- **Core interfaces** — `IActivityLogger`, `IOllamaClientFactory`, config models; see [hound-core.instructions.md](.github/instructions/hound-core.instructions.md)
- **Activity flow**: Nodes → `HttpActivityLogger` → API → RavenDB → SignalR → Dashboard
- **Keyed `IChatClient`** — `"strategy"` (larger model) vs `"default"` (standard); registered in pack `Program.cs`
- **`IOptions<T>`** for configuration binding; externalized hound configs in `Config/*.json`
- **Records** for node output DTOs in `Nodes/NodeModels.cs`; config models in `Hound.Core/Models/HoundConfigs.cs`
- **Controllers** use `[ApiController]`, `[Route("api/[controller]")]`, `CancellationToken` on all methods
- **Angular UI**: Spartan-ng (`@spartan-ng/brain` + `@spartan-ng/helm`) component library

## Solution Structure
```
src/
  Hound.Core/          — Shared models, interfaces (IActivityLogger, IOllamaClientFactory), logging
  Hound.Trading/       — Trading pack: Hounds/, Config/, Workflows/, AlpacaClient/, Services/
  Hound.Api/           — REST API + SignalR: Controllers/, Hubs/, Repositories/, Services/
  Hound.Core.Tests/    — MSTest unit tests for core library
  Hound.Trading.Tests/ — MSTest unit tests for trading logic
  Hound.Api.Tests/     — MSTest unit tests for API
  Hound.Eval/          — Eval harness: Scenarios/{HoundName}/*.json
ui/
  hound-dashboard/     — Angular SPA: pages/, services/, models/
```

## Code Style
- C#: 4 spaces, file-scoped namespaces, nullable enabled, `TreatWarningsAsErrors`
- TypeScript: 2 spaces, standalone components, signals where appropriate
- YAML: 2 spaces
- All files: UTF-8, LF line endings, trim trailing whitespace

## When Adding a New Hound
1. Create the hound class in the pack's `Hounds/` directory
2. Create a JSON config file in the pack's `Config/` directory
3. Register the hound as singleton in the pack's `Program.cs`
4. Add to the pack's workflow graph
5. Create ≥5 eval scenarios in `Hound.Eval/Scenarios/{HoundName}/` (see hound-eval skill)
6. Add MSTest unit tests
7. Validate with `dotnet run --project src/Hound.Eval -- --dry-run`

## CI/CD
- **`build-and-push.yml`** — On push/PR: restore, build, test (.NET + Angular); builds + pushes Docker images to GHCR on `main`
- **`eval.yml`** — Manual `workflow_dispatch` to run eval scenarios with Ollama

## Customization Assets
- **Instructions**: `.github/instructions/` — scoped guides for API, Angular, C#, Docker, tests, services, core, graph workflows
- **Skills**: `.github/skills/hound-eval/`, `.github/skills/csharp-mstest/`
- **Agents**: `.github/agents/reviewer.agent.md` — read-only convention reviewer
- **Prompts**: `.github/prompts/new-hound.prompt.md`, `.github/prompts/new-pack.prompt.md`
- **Hooks**: `.github/hooks/eval-reminder.json`, `model-sync-reminder.json`

## Do NOT
- Commit secrets or API keys (use `.env` / user-secrets)
- Hardcode URLs (use configuration + environment variables)
- Use `Console.WriteLine` for logging (use `IActivityLogger` or `ILogger`)
- Skip eval scenarios for new hounds
