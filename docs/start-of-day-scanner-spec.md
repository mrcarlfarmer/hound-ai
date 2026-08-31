# Start-of-Day Scanner — Specification / Plan

> Status: **Draft plan for review** (no implementation yet). This document investigates the
> "start-of-day scanner" concept from issue #52 and proposes how it could be built on the
> existing Hound AI platform. It defines **what** the feature is, **where** the signals come
> from, and **how** it would slot into the current trading pack, API and dashboard — but it
> deliberately stops short of code. Treat it as the instruction source when you later ask
> Copilot to begin implementation.

---

## 1. Overview

The **Start-of-Day (SoD) Scanner** is a new capability that runs around the market open (and
optionally pre-market) to **discover which symbols are worth trading today** rather than relying
on the current hard-coded `TradingGraph:Symbols` list. It ingests broad-market data, ranks
symbols by momentum / volume / gap / volatility signals, has a **Scanner hound** explain *why*
each candidate surfaced, and produces two artefacts:

1. A **scan-results page** in the dashboard showing ranked candidates with the signals and a
   short natural-language rationale for each.
2. A **watchlist** of symbols to keep on the radar, which becomes the symbol source that feeds
   the existing entry-phase graph (Analysts ▸ Data ▸ Strategy ▸ Risk ▸ Approval ▸ Execution).

Today the pipeline processes a static list configured in `TradingGraphSettings.Symbols`
(`src/Hound.Trading/Graph/TradingGraph.cs:15`) on a schedule
(`TradingWorker`, `src/Hound.Trading/TradingWorker.cs:83-91`), or one-off `RunRequest`s from the
dashboard. The scanner replaces the "where does the symbol list come from?" step with a
data-driven, explainable process while **reusing all existing infrastructure**: Docker Compose,
Ollama (local LLMs), RavenDB (activity + document storage), the `hound-api` REST + SignalR layer,
and the Angular dashboard.

The scanner distinguishes **trade styles** so downstream strategy can be intent-aware:

- **Scalping** — high relative volume, tight spreads, intraday momentum, news catalysts.
- **Day-trade / gap** — gap-up / gap-down vs previous close, pre-market volume, opening drive.
- **Swing** — multi-day trend, breakout above resistance, sector strength.

---

## 2. Goals & non-goals

### Goals
- Replace the static symbol list with a **data-driven, explainable** universe selected each day.
- Detect and rank **momentum, relative volume, gap-up/gap-down, volatility, and news-catalyst**
  signals near the open.
- Classify each candidate by **opportunity type** (scalp / day / swing) with a confidence score.
- Surface a **scan-results page** with the ranked candidates and per-symbol **reasoning**.
- Maintain a **watchlist** (auto-populated from top scans + manual pins) that drives which symbols
  the trading graph analyses.
- Keep everything **local and offline-capable** where data providers allow; no new secrets beyond
  the existing Alpaca keys unless an optional provider is enabled.
- Be **explainable and auditable** — every candidate's signals and rationale land in the RavenDB
  activity log and are visible in the dashboard.

### Non-goals / hard exclusions
- **The scanner does not place trades.** It only produces candidates + a watchlist; the existing
  Risk and human-in-the-loop Approval gates remain the sole path to execution.
- Not a real-time tick-by-tick scanner in v1 — it runs on a schedule (pre-market + open, then
  optional intraday refresh), not a streaming firehose.
- No new paid market-data subscription is required for v1 (uses Alpaca data already in the stack);
  richer feeds are **optional** providers behind config flags.
- Does not change the risk model, position sizing, or execution logic.

---

## 3. Hard requirements (must-haves)

| # | Requirement |
|---|-------------|
| R1 | **Scan produces a ranked, explainable candidate list.** Each candidate carries its raw signals (gap %, relative volume, momentum, etc.), a computed score, an opportunity type, and a one-line rationale. |
| R2 | **Scanner never trades.** Its only outputs are scan results + watchlist entries + `RunRequest`s; execution still flows through Risk → Approval → Execution unchanged. |
| R3 | **Scan results are persisted** to RavenDB (as documents) and surfaced live via SignalR to the dashboard. Every scan run is also written to the activity log via `IActivityLogger`. |
| R4 | **Watchlist is durable and editable** — symbols can be auto-added by the scanner and manually pinned/removed from the dashboard; pinned symbols survive daily re-scans. |
| R5 | **Data sources are pluggable** behind an `IScanSource` interface; at least the Alpaca-backed source (movers / most-actives + bars) works with the existing keys and no extra secrets. |
| R6 | **Scanner honours the market clock** — pre-market and open scans are gated by `IAlpacaService.GetClockAsync` so it does not scan a closed/holiday market. |
| R7 | **Configurable universe & thresholds** — min price, min average volume, min gap %, max symbols, exchange filters, and opportunity-type toggles live in config (`Config/ScannerNode.json` + `TradingGraph`/`Scanner` settings), not hard-coded. |
| R8 | **The watchlist becomes the symbol source** for scheduled runs (opt-in), replacing / augmenting the static `Symbols` list without breaking existing behaviour. |
| R9 | **No secrets committed.** Any optional provider keys come from `.env` / user-secrets, following the existing Alpaca pattern. |

---

## 4. Signals & how to detect them

The scanner computes a set of **primitive signals** per symbol, then combines them into a weighted
score (mirroring the existing `DataNode.json` `IndicatorWeights` pattern,
`src/Hound.Trading/Config/DataNode.json:7-11`).

| Signal | Definition | Primary source |
|--------|-----------|----------------|
| **Gap %** | `(today_open or last pre-market price − prev_close) / prev_close` | Alpaca bars (daily prev close + latest trade/minute bar) |
| **Relative volume (RVOL)** | current cumulative volume ÷ average volume for same time-of-day over N days | Alpaca minute/daily bars |
| **Momentum** | short-window return (e.g. 1/5/15-min ROC) and multi-day trend | Alpaca bars (reuses `DataNode` trend calc) |
| **Volatility / ATR** | average true range or intraday range vs price | Alpaca bars |
| **Range position** | price vs prior-day high/low, premarket high/low, VWAP | Alpaca bars |
| **News catalyst** | fresh headline in the pre-market window | existing news providers (see §5) |
| **Sentiment** | social buzz spike | existing `StockTwitsSentimentService` |
| **Liquidity / spread** | avg volume, dollar-volume, quote spread (tradability filter) | Alpaca bars / assets |

**Opportunity classification** (rule + LLM hybrid):
- **Scalp** — high RVOL + tight spread + strong intraday momentum + (optional) news.
- **Gap / day** — abs(gap %) above threshold + elevated pre-market volume.
- **Swing** — multi-day breakout / trend strength + sector confirmation.

The **numeric signals are computed deterministically in code** (fast, cheap, testable). The
**Scanner hound (LLM)** then writes the human-readable rationale and refines the opportunity-type
label, exactly like the split between arithmetic pre-computation and LLM reasoning already used in
`RiskNode` (`src/Hound.Trading/Nodes/RiskNode.cs:99-123`) and `StrategyNode`.

---

## 5. Data sources

The platform already ships several market-intel sources; the scanner should **reuse them first**
and add screener-style discovery on top.

### Already in the repo (reuse)
- **Alpaca Data API** via `IAlpacaService` (`src/Hound.Trading/AlpacaClient/AlpacaService.cs`):
  `GetBarsAsync`, `GetLatestTradeAsync`, `GetClockAsync`, `GetAssetAsync`, `ListNewsAsync`.
- **News**: `AlpacaNewsProvider`, `GoogleNewsRssProvider`, `YahooFinanceRssProvider`
  (`src/Hound.Trading/Services/News/`).
- **Sentiment**: `StockTwitsSentimentService` (`src/Hound.Trading/Services/`).

### New discovery sources (behind `IScanSource`)
1. **Alpaca Screener / Market Movers & Most-Actives** (top gainers/losers, most-active by volume &
   trade count) — no extra credentials, works with existing Alpaca keys. Primary v1 source.
2. **A configured base universe** (e.g. S&P 500 / Nasdaq-100 / a curated CSV in `Config/`) that the
   scanner filters and ranks — deterministic, offline, good for tests and dry-runs.
3. **Optional/extensible** (flagged off by default, documented for later):
   - Finnhub / Financial Modeling Prep / Polygon screeners (require a free/paid key in `.env`).
   - Nasdaq / Yahoo pre-market movers RSS/HTML (best-effort, like the existing RSS providers).

Each source implements a small contract so the mix is config-driven and testable with stubs
(mirroring how eval stubs already work, `src/Hound.Eval/`):

```csharp
public interface IScanSource
{
    string Name { get; }
    Task<IReadOnlyList<ScanCandidate>> DiscoverAsync(ScanContext context, CancellationToken ct);
}
```

---

## 6. Architecture & where it lives

The scanner is **not a new container** — it fits inside the existing `trading-pack` process,
following the one-container-per-pack rule. It is a scheduled step in `TradingWorker` plus (option B)
a graph phase. Two implementation shapes are on the table:

### Option A — Scanner as a pre-graph service (recommended for v1)
A `ScannerService` (hosted logic invoked by `TradingWorker`) runs on the SoD schedule, calls the
`IScanSource`s, computes signals, asks the **Scanner hound** for rationales, writes `ScanRun` /
`ScanCandidate` / `WatchlistEntry` documents to RavenDB, logs activity, and enqueues `RunRequest`s
(or updates the watchlist that scheduled runs read from). Minimal change to the graph; the scanner
is a **producer** of symbols.

```mermaid
flowchart LR
  subgraph trading-pack
    SW[TradingWorker\nschedule + clock gate]
    SS[ScannerService]
    SH[Scanner hound\nrationale + classification]
    G[TradingGraph\nAnalysts ▸ Data ▸ Strategy ▸ Risk ▸ Approval ▸ Execution]
  end
  SRC[(IScanSource[]\nAlpaca movers / universe / news / sentiment)]
  OL[ollama]
  RV[(RavenDB\nScanRun / ScanCandidate / Watchlist)]
  API[hound-api]
  UI[hound-ui\nScanner page + Watchlist]

  SW --> SS
  SS --> SRC
  SS --> SH --> OL
  SS --> RV
  SS -->|watchlist / RunRequests| G
  SS -->|IActivityLogger| API --> RV
  API -->|SignalR OnScanUpdate / OnWatchlistUpdate| UI
```

### Option B — Scanner as a new graph phase / node
Add a `ScannerNode` (`INode`) and a `GraphPhase.Scan` that runs before entry, emitting candidate
symbols that fan out into per-symbol entry runs. More consistent with the graph-first design and
checkpoint/resume, but a bigger change to routing (`TradingGraph.cs:333-346`) and state
(`TradingGraphState`). **Proposed as a fast-follow after Option A proves the signals.**

Either way the **Scanner hound** is a first-class hound (per the naming conventions) registered in
`Program.cs`, with a `Config/ScannerNode.json`, eval scenarios, and unit tests — following the
"When Adding a New Hound" checklist in the repo instructions.

---

## 7. The Scanner hound

- **Role**: given the deterministically-computed signal bundle for a shortlist of symbols, produce
  (a) a concise **rationale** per symbol, (b) a refined **opportunity type**, and (c) an optional
  **overall market note** (risk-on/off, sector leadership).
- **Model**: the standard `"default"` keyed `IChatClient` is sufficient (structured summarisation,
  not heavy reasoning); the larger `"strategy"` model is unnecessary and slower.
- **Output DTO** (record in `Nodes/NodeModels.cs` style):

```csharp
public record ScanCandidate(
    string Symbol,
    decimal LastPrice,
    decimal GapPercent,
    decimal RelativeVolume,
    decimal Momentum,
    decimal AtrPercent,
    double Score,
    ScanOpportunityType OpportunityType,  // Scalp | Day | Swing
    string Rationale,
    IReadOnlyList<string> Signals);

public record ScanRun(
    string Id,
    DateTimeOffset RanAt,
    string Trigger,                        // PreMarket | Open | Intraday | Manual
    IReadOnlyList<ScanCandidate> Candidates,
    string? MarketNote);
```

The hound follows the existing conventions: injects `IActivityLogger`, emits a start/finish
activity, returns strict JSON, and never calls `Console.WriteLine`.

---

## 8. Watchlist

- Stored as `WatchlistEntry` documents in RavenDB (in the trading pack DB, alongside trades/runs).
- Fields: `Symbol`, `Source` (`Scanner` | `Manual`), `Pinned`, `AddedAt`, `LastScore`,
  `LastOpportunityType`, `Notes`, `Expiry`/TTL for auto-added entries.
- **Auto-population**: after each scan, the top-N candidates above a score threshold are upserted;
  stale auto-entries (not seen in the last M scans and not pinned) age out.
- **Manual control**: pin/unpin, add/remove, and annotate from the dashboard.
- **Feeds the pipeline**: a new `SymbolSource` config option lets scheduled runs read symbols from
  the watchlist (`WatchlistSymbolSource`) instead of / in addition to the static `Symbols` list —
  satisfying R8 without breaking the existing static behaviour (default stays static).

---

## 9. API & real-time surface (`hound-api`)

New controller + models, following the existing controller conventions (`[ApiController]`,
`[Route("api/[controller]")]`, `CancellationToken` on every method, repository pattern):

| Endpoint | Purpose |
|----------|---------|
| `GET /api/scanner/runs` | List recent scan runs (paged) |
| `GET /api/scanner/runs/{id}` | One scan run with candidates + reasoning |
| `GET /api/scanner/runs/latest` | Most recent scan (for the page's default load) |
| `POST /api/scanner/runs` | Trigger an on-demand scan (like `POST /api/runs`) |
| `GET /api/watchlist` | Current watchlist |
| `POST /api/watchlist` | Add / pin a symbol |
| `DELETE /api/watchlist/{symbol}` | Remove a symbol |
| `PATCH /api/watchlist/{symbol}` | Pin/unpin, edit notes |

New SignalR hub events (extending the existing `activity` hub): `OnScanUpdate` (new scan run) and
`OnWatchlistUpdate` (watchlist changed), consistent with `OnActivity` / `OnOrderUpdate` /
`OnGraphRunUpdate`.

Keep the Angular `services/` and `models/` in sync with these endpoints/events per the
angular-services instructions.

---

## 10. Dashboard (Angular)

Two additions to the SPA (standalone components, lazy-loaded routes in `app.routes.ts`,
Spartan-ng + Tailwind styling, vitest tests):

1. **`/scanner` — Scan results page**
   - Header: last-scan time, trigger, "Run scan now" button, market note.
   - Ranked table of candidates: symbol, score, opportunity-type badge (scalp/day/swing),
     gap %, RVOL, momentum, ATR, sparkline, and an expandable **rationale**.
   - Filters: opportunity type, min score, min RVOL, sector; sort by any signal.
   - Row actions: **Add to watchlist**, **Run now** (creates a `RunRequest`), **Open chart**
     (links to the existing `/charts` explorer).
   - Live updates via `OnScanUpdate`.

2. **`/watchlist` — Watchlist page** (or a panel on the scanner page)
   - Pinned + auto entries, with score/opportunity trend, notes, pin/unpin, remove.
   - "Include watchlist in scheduled runs" toggle (writes the `SymbolSource` setting).
   - Live updates via `OnWatchlistUpdate`.

Add both routes to the nav and to the README dashboard table (7 pages → 9).

---

## 11. Configuration

- **`src/Hound.Trading/Config/ScannerNode.json`** — hound model/instructions/temperature +
  **signal weights** and **thresholds** (min price, min avg volume, min gap %, max candidates,
  score cut-off for auto-watchlist), mirroring `DataNode.json`.
- **`Scanner` settings section** (bound via `IOptions<ScannerSettings>`): enabled sources, schedule
  (pre-market time, open offset, intraday refresh interval), `SymbolSource`
  (`Static` | `Watchlist` | `Both`), universe file path, exchange filters.
- **Optional provider keys** (Finnhub/FMP/Polygon) via `.env` / user-secrets only — never committed.

Defaults must preserve current behaviour: scanner **disabled** by default, `SymbolSource = Static`,
so existing deployments are unchanged until opted in.

---

## 12. Evals & tests

Follow the repo's testing conventions (MSTest + Moq for .NET, vitest for Angular) and the
hound-eval skill:

- **≥5 eval scenarios** in `src/Hound.Eval/Scenarios/ScannerNode/` — e.g. clear gap-up momentum,
  high-RVOL scalp, multi-day swing breakout, low-liquidity reject, flat/no-signal day. Stubs feed
  deterministic bar/movers data so scenarios run under `--dry-run` with no LLM/network.
- **Unit tests**: signal math (gap %, RVOL, momentum, ATR) with fixed fixtures; `IScanSource`
  stubs; watchlist upsert/age-out logic; `ScannerController` + repository; opportunity
  classification thresholds.
- **Angular tests**: scanner table rendering, filters/sort, watchlist actions, SignalR handlers.
- Validate scenarios offline with `dotnet run --project src/Hound.Eval -- --dry-run`.

---

## 13. Additional ideas / concepts to consider wrapping in

These are **optional extensions** proposed for discussion — not committed scope:

1. **Sector / breadth context** — compute sector ETF strength and market breadth (advancers vs
   decliners) so the Scanner hound's "market note" is grounded, and swing candidates are confirmed
   by sector leadership.
2. **End-of-day recap & scan scoring feedback loop** — record which scanned candidates actually
   moved / were traded and the outcome, so scan weights can be tuned. This pairs naturally with the
   existing **TunerHound** autoresearch mechanism (suggest/apply config changes).
3. **Alerting** — push notable candidates (huge gap + news) to the activity feed with high severity,
   or out to a channel (Telegram/webhook) reusing patterns proposed in the grocery-pack spec.
4. **Pre-market vs open two-stage scan** — a wide pre-market shortlist, then a tighter re-rank at
   the open using opening-drive/VWAP behaviour.
5. **Halt / LULD & earnings awareness** — flag symbols with trading halts, LULD bands, or earnings
   today so risky candidates are annotated (or filtered).
6. **Per-opportunity strategy hints** — pass the opportunity type into `StrategyNode` context so a
   scalp candidate gets tighter targets/stops than a swing candidate.
7. **Manual seed lists / themes** — let the user drop a themed list (e.g. "AI names", "biotech
   catalysts") into the universe for the scanner to rank.
8. **Backtest / replay mode** — run the scanner against a historical date to validate signal logic
   offline, reusing the eval stub infrastructure.

---

## 14. Risks & open items (confirm before/at implementation)

- **Data availability & rate limits** — Alpaca movers/most-actives coverage and pre-market data
  quality on the paper plan; confirm which endpoints are available and their limits.
- **Universe size vs single-GPU throughput** — `MaxDegreeOfParallelism = 1` means many candidates
  can't all run the full entry graph; the scanner must cap how many symbols become `RunRequest`s.
- **Opportunity-type thresholds** — need real data to calibrate scalp/day/swing cut-offs; start
  conservative and tune via the feedback loop (§13.2).
- **Scheduling & timezones** — align the SoD schedule to market timezone using the Alpaca clock,
  not the container clock; handle holidays/half-days.
- **Option A vs B** — decide whether the scanner stays a pre-graph service (v1) or becomes a graph
  phase (fast-follow), and whether watchlist replaces or augments the static list by default.
- **Persistence footprint** — retention/TTL for `ScanRun` documents (potentially many per day).

---

## 15. Acceptance criteria (v1)

- A scheduled (and on-demand) scan runs, gated by the market clock, and produces a ranked,
  explainable candidate list persisted to RavenDB and logged via `IActivityLogger`.
- The **`/scanner` page** shows ranked candidates with signals + per-symbol rationale, updating live.
- The **watchlist** can be auto-populated and manually edited, and can (opt-in) drive which symbols
  the scheduled trading graph analyses — with the default configuration leaving current behaviour
  unchanged.
- At least the Alpaca-backed `IScanSource` works with existing keys; sources are pluggable.
- ≥5 eval scenarios + unit tests pass offline; no secrets committed.

---

## 16. Suggested implementation phases

1. **Signals core (offline)** — `IScanSource` + Alpaca/universe sources + deterministic signal
   math + `ScanCandidate`/`ScanRun` models + unit tests. No LLM, no UI.
2. **Scanner hound** — `ScannerNode`/hound, `Config/ScannerNode.json`, rationale + classification,
   eval scenarios, register in `Program.cs`.
3. **Persistence + schedule** — RavenDB documents, `ScannerService` wired into `TradingWorker` with
   clock gating and activity logging.
4. **API + SignalR** — `ScannerController`, watchlist endpoints, `OnScanUpdate` /
   `OnWatchlistUpdate` events, repositories + indexes.
5. **Dashboard** — `/scanner` and `/watchlist` pages, services/models, nav + README updates.
6. **Watchlist → pipeline** — `SymbolSource` config so scheduled runs can consume the watchlist.
7. **Fast-follows** — sector/breadth context, feedback-loop tuning (TunerHound), Option B graph
   phase, alerting.
