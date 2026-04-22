# Hound AI

**Hound AI** is a local multi-agent platform for autonomous task packs — containerized, GPU-accelerated, and self-updating via GitOps. Agents are called **hounds**; groups of hounds are called **packs**.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        hound-net (bridge)                   │
│                                                             │
│  ┌──────────┐    ┌──────────────┐    ┌───────────────────┐  │
│  │  ollama  │    │   ravendb    │    │   trading-pack    │  │
│  │ :11434   │◄───│   :8080      │    │  (4 hounds)       │  │
│  │ (LLM)    │    │  (activity   │◄───│  Analysis         │  │
│  └──────────┘    │   logging)   │    │  Strategy         │  │
│       ▲          └──────────────┘    │  Risk             │  │
│       │                 ▲            │  Execution        │  │
│       └─────────────────┼────────────┘                   │  │
│                         │                                   │
│  ┌──────────┐    ┌──────────────┐    ┌───────────────────┐  │
│  │ hound-ui │    │  hound-api   │    │    watchtower     │  │
│  │  :4200   │───►│   :5000      │    │  (GitOps auto-    │  │
│  │ (Angular)│    │ (ASP.NET +   │    │   deploy/GHCR)    │  │
│  └──────────┘    │  SignalR)    │    └───────────────────┘  │
│                  └──────────────┘                           │
└─────────────────────────────────────────────────────────────┘

Data flow:  trading-pack ──► RavenDB ──► hound-api ──► hound-ui (SignalR)
```

---

## Tech Stack

| Component             | Technology                        | Version     |
|-----------------------|-----------------------------------|-------------|
| Agent Framework       | Microsoft Agent Framework         | v1.1.0      |
| Backend               | .NET / ASP.NET Core               | 9.0         |
| Frontend              | Angular (standalone components)   | 21          |
| Database              | RavenDB                           | Latest      |
| LLM                   | Ollama (containerized)            | Latest      |
| Trading API           | Alpaca Markets (paper)            | NuGet       |
| Orchestration         | Docker Compose                    | WSL2        |
| Real-time             | SignalR                           | Built-in    |
| Auto-deploy           | Watchtower                        | Latest      |

---

## Prerequisites

| Requirement              | Notes                                               |
|--------------------------|-----------------------------------------------------|
| WSL2                     | Ubuntu 22.04 LTS recommended                        |
| Docker Desktop           | GPU passthrough enabled (NVIDIA)                    |
| .NET 9 SDK               | `dotnet --version` should show `9.x`                |
| Node 22+                 | `node --version` should show `v22.x`                |
| Angular CLI              | `npm install -g @angular/cli`                       |
| NVIDIA GPU + drivers     | Required for Ollama GPU passthrough                 |

---

## Quick Start

```bash
# 1. Clone the repo
git clone https://github.com/mrcarlfarmer/hound-ai.git
cd hound-ai

# 2. Copy and fill in secrets
cp .env.example .env
# Edit .env — set ALPACA_API_KEY, ALPACA_API_SECRET, GHCR_TOKEN

# 3. Start everything
docker compose up -d

# 4. Wait for Ollama models to pull (ollama-init container)
docker compose logs -f ollama-init
```

After startup:
- Dashboard → http://localhost:4200
- API → http://localhost:5000
- RavenDB Studio → http://localhost:8080
- Ollama API → http://localhost:11434

---

## Development Setup

```bash
# Start dev stack with hot-reload for all services
docker compose -f docker-compose.yml -f docker-compose.dev.yml up
```

This override:
- Runs `dotnet watch run` inside `trading-pack` and `hound-api` containers
- Mounts your local `src/` directories into the containers for live reload
- Replaces nginx in `hound-ui` with `ng serve` for Angular live reload
- Sets `ASPNETCORE_ENVIRONMENT=Development`

### .NET User Secrets (local dev without Docker)

```bash
cd src/Hound.Trading
dotnet user-secrets set "Alpaca:ApiKey" "your_key"
dotnet user-secrets set "Alpaca:ApiSecret" "your_secret"

cd src/Hound.Api
dotnet user-secrets set "RavenDb:Url" "http://localhost:8080"
```

---

## GHCR PAT Setup (Watchtower)

Watchtower polls GHCR every 5 minutes and redeploys updated images automatically.

1. Go to **GitHub → Settings → Developer Settings → Personal access tokens (classic)**
2. Generate a new token with the **`read:packages`** scope
3. Set `GHCR_TOKEN` in your `.env` file (copied from `.env.example` in step 2 of Quick Start)

The `docker-compose.yml` passes this token to Watchtower as `REPO_PASS` automatically.

> **Never commit your PAT.** The `.env` file is listed in `.gitignore`.

---

## Environment Variable Reference

| Variable             | Description                               | Example                            |
|----------------------|-------------------------------------------|------------------------------------|
| `ALPACA_API_KEY`     | Alpaca Markets paper trading API key      | `PKxxxxxxxxxxxxxxxx`               |
| `ALPACA_API_SECRET`  | Alpaca Markets paper trading secret       | `xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx` |
| `ALPACA_BASE_URL`    | Alpaca base URL (paper or live)           | `https://paper-api.alpaca.markets` |
| `OLLAMA_MODEL`       | Default Ollama model name                 | `gemma3`                           |
| `RAVENDB_URL`        | RavenDB connection URL                    | `http://ravendb:8080`              |
| `GHCR_TOKEN`         | GitHub PAT for GHCR (read:packages)       | `ghp_xxxxxxxxxxxx`                 |

Copy `.env.example` to `.env` and fill in your values. The `.env` file is git-ignored.

---

## Project Structure

```
hound-ai/
├── docker-compose.yml          # Production stack (6 containers)
├── docker-compose.dev.yml      # Dev overrides (hot-reload)
├── .env.example                # Environment variable template
├── infra/
│   ├── ollama/
│   │   └── pull-models.sh      # Bootstrap: pulls qwen3:14b, qwen3.5:9b
│   └── watchtower/
│       └── config.env          # Watchtower poll interval settings
├── src/
│   ├── Hound.Core/             # Shared models, interfaces, IActivityLogger
│   ├── Hound.Trading/          # Trading pack (4 hounds + AF workflow)
│   ├── Hound.Api/              # Monitoring REST API + SignalR hub
│   ├── Hound.Core.Tests/
│   ├── Hound.Trading.Tests/
│   ├── Hound.Api.Tests/
│   └── Hound.Eval/             # Agent evaluation harness
└── ui/
    └── hound-dashboard/        # Angular 21 SPA
```

---

## Naming Conventions

| Term    | Meaning                                 |
|---------|-----------------------------------------|
| Hound   | An individual AI agent                  |
| Pack    | A group of hounds sharing a container   |

---

## CI / GitOps

- **CI pipeline**: `.github/workflows/` — builds, tests, and publishes Docker images to GHCR on every push to `main`
- **Watchtower**: polls GHCR every 5 minutes; if a new image is found for any running container it pulls and performs a rolling restart
- **Ollama bootstrap**: the `ollama-init` container runs once after `ollama` is healthy and pulls all configured models, then exits

---

## License

This project is for educational and paper-trading purposes only. Not financial advice.
