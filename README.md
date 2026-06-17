# Hound AI

**Hound AI** is a local multi-agent platform for autonomous task packs — containerized, GPU-accelerated, and self-updating via GitOps. Agents are called **hounds**; groups of hounds are called **packs**.

---

## Architecture

Six containers run on a single `hound-net` bridge network:

```
┌────────────────────────────────────────────────────────────────────┐
│                         hound-net (bridge)                         │
│                                                                    │
│  ┌──────────┐   ┌──────────────┐   ┌─────────────────────────────┐ │
│  │  ollama  │   │   ravendb    │   │        trading-pack         │ │
│  │ :11434   │◄──│   :8080      │◄──│   Graph pipeline of hounds: │ │
│  │ (LLM)    │   │ (activity +  │   │   Analysts ▸ Data ▸ Strategy│ │
│  └────▲─────┘   │  graph runs +│   │   ▸ Risk ▸ Approval ▸        │ │
│       │         │  trades)     │   │   Execution ▸ Monitor (loop) │ │
│  ┌────┴───────┐ └──────┬───────┘   └──────────────┬──────────────┘ │
│  │ ollama-init│        │                          │                │
│  │ (pull      │        │   activity / node-stream │                │
│  │  models,   │        │   events  ◄──────────────┘                │
│  │  one-shot) │        ▼                                           │
│  └────────────┘ ┌──────────────┐        ┌──────────────┐           │
│                 │  hound-api   │◄───────│   hound-ui   │           │
│                 │   :5000      │  REST  │    :4200     │           │
│                 │ (ASP.NET +   │───────►│  (Angular)   │           │
│                 │  SignalR)    │ SignalR└──────────────┘           │
│                 └──────────────┘                                   │
└────────────────────────────────────────────────────────────────────┘

Data flow:  trading-pack ──► RavenDB ──► hound-api ──► hound-ui (SignalR)
            trading-pack ──► hound-api (activity + live node-stream events)
```

---

## Features

- **Graph-based trading pipeline** — a cyclic state machine of hounds (Analysts team ▸ Data ▸ Strategy ▸ Risk ▸ Approval ▸ Execution ▸ Monitor) with checkpoint/resume backed by RavenDB.
- **Multi-analyst research** — Market, Fundamentals, News and Sentiment analysts run per symbol and are merged by a synthesiser into a single market analysis.
- **Bull-vs-bear strategy debate** — the Strategy hound can run a configurable debate before committing to a Buy/Sell/Hold decision; the full transcript is surfaced in the dashboard.
- **Human-in-the-loop approval** — trades pause at an Approval gate and wait for an explicit approve/reject (with notes) from the dashboard before execution.
- **Position monitoring loop** — after a fill, the Monitor hound polls Alpaca for fills and P&L and loops back to refresh analysis while the trade is open.
- **Protective trailing stops** — broker trailing stops for whole shares and a software-emulated trailing stop (high-water-mark poller) for fractional shares.
- **Live graph-run explorer** — node-by-node status, timing, errors and streaming LLM tokens in real time.
- **Portfolio & execution views** — account equity/cash/buying power, open positions with unrealized P&L, trade history, Alpaca sync and one-click close.
- **Tuner experiments** — review and apply/reject suggested hound config changes.
- **OHLCV chart explorer** — symbol + timeframe selection rendered with lightweight-charts.
- **Market Intel sources** — Alpaca news + bars, Google News RSS, Yahoo Finance RSS, and StockTwits sentiment.
- **Eval harness** — 30+ JSON scenarios across the Data, Strategy, Risk, Execution and Monitor hounds, runnable offline with `--dry-run`.
- **Real-time dashboard** — SignalR pushes activity, order updates, graph-run snapshots and node-stream tokens to the Angular SPA.

---

## Trading Pipeline

The trading pack runs a cyclic graph of hounds (implemented as graph nodes). A run flows through two phases:

**Entry phase**

1. **Analysts team** — Market, Fundamentals, News and Sentiment analysts run per symbol; a synthesiser merges them into a `MarketAnalysis` (skipped onward if confidence is below the configured minimum).
2. **Data** — fetches market bars and derives trend, volume change and a confidence score.
3. **Strategy** — decides Buy/Sell/Hold with quantity and confidence using the larger `"strategy"` model; runs an optional bull-vs-bear debate first. `Hold` ends the run.
4. **Risk** — validates the trade against position/exposure/share limits. `Modified` loops back to Strategy (up to `MaxRefinements`); a hard exposure-cap breach is `Rejected`.
5. **Approval** — a human-in-the-loop gate; the run pauses until an approve/reject decision is written from the dashboard.
6. **Execution** — places the order via Alpaca (market/limit + time-in-force), attaches protective stops, and persists a `TradeDocument` to RavenDB.

**Monitor phase**

7. **Monitor** — polls Alpaca for fill status and P&L, advances software trailing stops, and loops back to the Analysts team to refresh while the position is open; ends when the trade is closed.

Runs are checkpointed via an `IStateStore` (RavenDB), so a pack can resume in-progress runs after a restart.

---

## Tech Stack

| Component             | Technology                        | Version     |
|-----------------------|-----------------------------------|-------------|
| Agent Framework       | Microsoft Agent Framework (`Microsoft.Agents.AI`) | 1.1.0 |
| Backend               | .NET / ASP.NET Core               | 9.0         |
| Frontend              | Angular (standalone components)   | 21          |
| UI styling            | Tailwind CSS + Spartan-ng         | 4 / alpha   |
| Charts                | lightweight-charts                | 5           |
| Database              | RavenDB (`RavenDB.Client`)        | 7.2         |
| LLM                   | Ollama (containerized)            | Latest      |
| Trading API           | Alpaca Markets (`Alpaca.Markets`) | 7.2         |
| Orchestration         | Docker Compose                    | WSL2        |
| Real-time             | SignalR + `@microsoft/signalr`    | Built-in    |
| .NET tests            | MSTest + Moq                      | —           |
| Angular tests         | vitest + jsdom                    | —           |

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
# Edit .env — set ALPACA_API_KEY, ALPACA_API_SECRET

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

## Dashboard

The Angular SPA exposes seven pages:

| Route          | Page              | What it shows                                                                 |
|----------------|-------------------|-------------------------------------------------------------------------------|
| `/`            | Dashboard         | Service health strip (Ollama, RavenDB, Trading Pack, API) and pack cards      |
| `/packs/:id`   | Pack detail       | Hounds in the pack, live activity feed, and the bull-vs-bear strategy debate  |
| `/activity`    | Activity log      | Filterable, paginated activity feed (by pack, hound, date range)              |
| `/execution`   | Execution         | Trade table with fill status, Alpaca sync, and close-position actions         |
| `/graph`       | Graph runs        | Run explorer with node snapshots, live LLM token streams, and approval UI     |
| `/portfolio`   | Portfolio         | Account summary, open positions with unrealized P&L, and close buttons        |
| `/charts`      | Charts            | OHLCV chart explorer with symbol + timeframe selection                        |

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

## Environment Variable Reference

| Variable             | Description                               | Example                            |
|----------------------|-------------------------------------------|------------------------------------|
| `ALPACA_API_KEY`     | Alpaca Markets paper trading API key      | `PKxxxxxxxxxxxxxxxx`               |
| `ALPACA_API_SECRET`  | Alpaca Markets paper trading secret       | `xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx` |
| `ALPACA_BASE_URL`    | Alpaca base URL (paper or live)           | `https://paper-api.alpaca.markets` |
| `OLLAMA_MODEL`       | Default Ollama model name                 | `qwen3.5:9b`                       |
| `RAVENDB_URL`        | RavenDB connection URL                    | `http://ravendb:8080`              |

Inside the containers these are bound via .NET configuration conventions — `docker-compose.yml` sets `Ollama__BaseUrl`, `RavenDb__Url`, and the trading pack reads Alpaca keys from the `.env` file.

Copy `.env.example` to `.env` and fill in your values. The `.env` file is git-ignored.

---

## API & Real-time Reference

The `hound-api` service exposes a REST API plus a SignalR hub at `/hubs/activity`.

| Endpoint group | Routes                                                                                      |
|----------------|---------------------------------------------------------------------------------------------|
| Health         | `GET /api/health`                                                                           |
| Packs          | `GET /api/packs`, `/api/packs/{id}`, `/api/packs/{packId}/hounds`, `POST /api/packs/register` |
| Activity       | `GET /api/activity`, `POST /api/activity`                                                    |
| Graph runs     | `GET /api/runs`, `/api/runs/{runId}`, `/api/runs/requests`, `POST /api/runs`                 |
| Run events     | `POST /api/runs/events/node-completed`, `/api/runs/events/node-stream`                       |
| Trades         | `GET /api/trades`, `/api/trades/{id}`, `POST /api/trades/order-update`                       |
| Portfolio      | `GET /api/portfolio/account`, `/api/portfolio/positions`, `POST /api/portfolio/positions/{symbol}/close` |
| Charts         | `GET /api/charts/{symbol}`                                                                   |
| Tuner          | `GET /api/tuner/experiments`, `/api/tuner/experiments/{id}`, `POST .../apply`, `POST .../reject` |

SignalR clients call `SubscribeToPack` / `UnsubscribeFromPack` and receive `OnActivity`, `OnOrderUpdate`, `OnGraphRunUpdate`, and `OnNodeStream` events.

---

## Project Structure

```
hound-ai/
├── docker-compose.yml          # Production stack (6 containers)
├── docker-compose.dev.yml      # Dev overrides (hot-reload)
├── .env.example                # Environment variable template
├── infra/
│   └── ollama/
│       └── pull-models.sh      # Bootstrap: pulls qwen3:14b, qwen3.5:9b
├── src/
│   ├── Hound.Core/             # Shared models, interfaces, IActivityLogger, LLM client
│   ├── Hound.Trading/          # Trading pack: graph pipeline of hounds (Graph/, Nodes/)
│   ├── Hound.Api/              # Monitoring REST API + SignalR hub
│   ├── Hound.Core.Tests/
│   ├── Hound.Trading.Tests/
│   ├── Hound.Api.Tests/
│   └── Hound.Eval/             # Eval harness: scenarios per hound (Data, Strategy, Risk, Execution, Monitor)
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
- **Ollama bootstrap**: the `ollama-init` container runs once after `ollama` is healthy and pulls all configured models, then exits

---

## License

This project is for educational and paper-trading purposes only. Not financial advice.
