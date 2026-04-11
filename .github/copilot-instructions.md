# Hound AI — Copilot Instructions

## Project Overview
Hound AI is a containerized multi-agent platform running locally in WSL2 via Docker Compose. It uses Microsoft Agent Framework (.NET) for agent orchestration, Ollama for local LLMs, RavenDB for activity logging, and an Angular + ASP.NET Core monitoring dashboard.

## Naming Conventions
- **Agents** are called **"Hounds"**
- **Agent groups** are called **"Packs"**
- The project name is **"Hound AI"**
- Use these terms consistently in code, comments, logs, and documentation

## Tech Stack
| Component | Technology | Version |
|-----------|-----------|---------|
| Agent Framework | Microsoft Agent Framework | v1.1.0 (`Microsoft.Agents.AI`) |
| Backend | .NET / ASP.NET Core | 9.0 |
| Frontend | Angular (standalone components) | 19+ |
| Database | RavenDB | Latest (`RavenDB.Client`) |
| LLM | Ollama (containerized) | Latest (`ollama/ollama`) |
| Trading API | Alpaca Markets (paper) | `Alpaca.Markets` NuGet |
| Orchestration | Docker Compose | WSL2 |
| Real-time | SignalR | ASP.NET Core built-in |
| Testing | MSTest + Moq (.NET), Karma + Jasmine (Angular) | |

## Architecture
6 Docker containers on a `hound-net` bridge network:
- `ollama` — Local LLM server (GPU passthrough, port 11434)
- `ravendb` — Document DB for activity logging (port 8080)
- `trading-pack` — Trading hounds: Analysis, Strategy, Risk, Execution
- `hound-api` — ASP.NET Core API (port 5000)
- `hound-ui` — Angular SPA via nginx (port 4200)
- `watchtower` — GitOps auto-deploy from GHCR

## Key Patterns
- **One container per pack** — all hounds in a pack share a process
- **AF graph-based workflows** for intra-pack orchestration
- **IActivityLogger** — all hounds log activity to RavenDB
- **IOllamaClientFactory** — creates LLM clients pointing at Ollama
- **SignalR** for real-time dashboard updates
- **IOptions<T>** for configuration binding
- **Externalized hound configs** in JSON files under `Config/`

## Solution Structure
```
src/
  Hound.Core/          — Shared models, interfaces, logging
  Hound.Trading/       — Trading pack (hounds, workflows, Alpaca)
  Hound.Api/           — Monitoring REST API + SignalR
  Hound.Core.Tests/    — MSTest: core library tests
  Hound.Trading.Tests/ — MSTest: trading logic tests
  Hound.Api.Tests/     — MSTest: API tests
  Hound.Eval/          — Agent evaluation harness
ui/
  hound-dashboard/     — Angular 19+ SPA
```

## Code Style
- C#: 4 spaces, file-scoped namespaces, nullable enabled, TreatWarningsAsErrors
- TypeScript: 2 spaces, standalone components, signals where appropriate
- YAML: 2 spaces
- All files: UTF-8, LF line endings, trim trailing whitespace

## When Adding a New Hound
1. Create the hound class in the pack's `Hounds/` directory
2. Create a JSON config file in the pack's `Config/` directory
3. Register the hound in the pack's `Program.cs`
4. Add to the pack's workflow
5. Create eval scenarios in `Hound.Eval/Scenarios/{HoundName}/`
6. Add unit tests

## Do NOT
- Commit secrets or API keys (use .env / user-secrets)
- Hardcode URLs (use configuration + environment variables)
- Use `Console.WriteLine` for logging (use `IActivityLogger` or `ILogger`)
- Skip eval scenarios for new hounds
