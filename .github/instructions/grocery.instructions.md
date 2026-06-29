---
description: "Use when editing the Grocery Pack hounds, nodes, graph, services, or config. Covers the shopping pipeline, the grocery-browser sidecar contract, the hard checkout/slot safety rule, durable markdown state, and settings patterns."
applyTo: "src/Hound.Grocery/**"
---
# Grocery Pack Conventions

The Grocery Pack (`Hound.Grocery`, display "Grocery Pack") does automated weekly
Sainsbury's grocery shopping. It runs as an **independent container pair** —
`grocery-pack` (.NET worker) + `grocery-browser` (Python `zendriver` sidecar) — and
reuses `Hound.Core` via a compile-time `ProjectReference` (NOT a shared service).
Do not modify or break the trading pack.

## Hard Safety Rule (R1) — non-negotiable
The system MUST NEVER select a delivery slot and MUST NEVER check out or pay.
- Enforced in the `grocery-browser` sidecar by two independent guards in
  `infra/grocery-browser/app/safety.py`: a **URL denylist** (checkout/payment/pay/
  slot/book-delivery/place-order/…) and an **action allowlist** (only
  search/open_product/set_quantity/add_to_basket/read_basket/read_order_history/
  screenshot/login). A breach raises `SafetyViolation` → HTTP 403.
- Every browser navigation must route through `navigate()` (URL guard); every RPC
  asserts its action against the allowlist.
- This rule has dedicated unit tests (`infra/grocery-browser/tests/test_safety.py`).
  Never weaken or bypass these guards, and never add a checkout/slot action.

## Hound Pipeline
Graph-based, cyclic: `ConciergeHound` → `PlannerHound` → `ShopperHound` →
`BudgetHound` → `LearnerHound`.
- **ConciergeHound** — Telegram persona (friendly/efficient); maintains a loose,
  self-learning shopping list in `shopping-list.md`.
- **PlannerHound** — turns the loose list into a concrete plan (`PlannedItem`s).
- **ShopperHound** — drives the `grocery-browser` sidecar; prefers Nectar prices +
  favourites; auto-substitutes out-of-stock items and records the reason.
- **BudgetHound** — pay-cycle budget with weekly ±10% flex; flag-and-ask on overage.
- **LearnerHound** — learns products/quantities over time.

Each hound implements the pack-local `INode` (`Graph/INode.cs`) and returns an
updated immutable `GroceryGraphState` (`Graph/GroceryGraphState.cs`). Node output
DTOs are records in `Nodes/NodeModels.cs`. Hounds are registered as singletons in
`Program.cs` and exposed via the node dictionary.

## grocery-browser Sidecar Contract
- The .NET side talks to the sidecar via `IBrowserWorkerClient` (`Services/`); wire
  DTOs mirror the Python pydantic models in `infra/grocery-browser/app/models.py`.
- RPC surface (spec 11.3): `/login`, `/search`, `/add`, `/set-quantity`, `/basket`,
  `/favourites`, `/order-history`, `/screenshot`, plus `/health`. Internal-only on
  `hound-net` at `http://grocery-browser:8090` (no host ports).
- Keep the .NET DTOs and the Python models in sync whenever the contract changes.
- **Selectors are externalised** to `infra/grocery-browser/app/selectors.json` — never
  hardcode Sainsbury's selectors in driver code. Bind to `data-testid` only (CSS classes
  are hashed; `data-pkgid` churns). The site spans two stacks: NEW `/groceries/` (React
  "fable", `gw-*` testids: search, product, favourites) and OLD `/gol-ui/` (Angular
  "Luna", `pt-*`/`trolley-item-*`/`order-summary-*`: trolley/basket, read-only).
- Navigation **waits on a selector, never network idle** (the new stack streams ads and
  never settles); cookie consent is `#onetrust-accept-btn-handler`; gol-ui needs ~9s to
  hydrate. `zendriver` is imported lazily in `app/browser.py` so parsing/safety logic is
  testable without Chrome.

## Hard Safety Rule R1 (never slot / never checkout)
- Enforced in `infra/grocery-browser/app/safety.py` by three guards: a **URL denylist**
  (checkout/payment/slot/book-delivery/…), a **testid denylist** (`gw-book-slot`,
  `order-summary-book-slot-button`, `book-delivery-button`, `book-delivery`), and an
  **action allowlist** (search/open_product/set_quantity/add_to_basket/read_basket/
  read_favourites/read_order_history/screenshot/login). Danger lists are sourced from
  `selectors.json` (`danger.denyTestIds`/`danger.denyUrlPatterns`) with a hardcoded
  fallback so the guard can never be silently disabled. Any breach → HTTP 403.
- `ShopperHound` HARD-STOPS once the basket is built; there is no `IBrowserWorkerClient`
  method that can reach a slot or checkout. The `DRY_RUN` kill switch (env `GROCERY_DRY_RUN`)
  makes `/add` + `/set-quantity` read-only.
- Always extend the pytest safety suite (assert denylisted testids/URLs return 403) when
  touching browser/selectors code.

## Durable State
- Stored as **local markdown files** on the git-ignored `grocery-data` volume
  (`/data/grocery` in-container): `shopping-list.md`, `preferences.md`,
  `budget-ledger.md`, `basket-trace.md`, `purchase-history.md`.
- Access through `Services/StateFileService` (use the `GroceryStateFile` enum; never
  hardcode file names). RavenDB is used for activity logging only, not durable state.

## Models (Ollama)
- Keyed `IChatClient` `default` and `vision` both → `gemma4:12b` (unified text+vision).
- Embeddings → `embeddinggemma` (product matching). Models are pulled by
  `infra/ollama/pull-models.sh`.

## Settings Classes
- All grocery Options live in `Config/GroceryOptions.cs` (NOT `Hound.Core`, to avoid
  touching the trading-shared core): `BudgetSettings`, `TelegramSettings`,
  `SainsburysSettings`, `ScheduleSettings`, `GroceryOllamaSettings`,
  `StateFileSettings`, `BrowserWorkerSettings`.
- Each has a `SectionName` constant matching its `appsettings.json` key; bind via
  `IOptions<T>`. Hound configs live in `Config/*.json`.

## Secrets
- Sainsbury's username/password and the Telegram bot token are git-ignored. In docker
  they come from `.env` env vars (`Sainsburys__Username`, `Telegram__BotToken`, …);
  for local dev copy `Config/secrets.grocery.example.json` →
  `Config/secrets.grocery.json` (git-ignored, loaded optionally in `Program.cs`).
- Never commit real secrets or hardcode credentials/URLs.

## Logging & Conventions
- Use `IActivityLogger` (or `ILogger`) — never `Console.WriteLine`. Activity flows
  Nodes → `HttpActivityLogger` → API → RavenDB → SignalR → dashboard.
- C#: 4 spaces, file-scoped namespaces, nullable enabled, `TreatWarningsAsErrors`
  (code must be warning-clean).

## When Adding / Changing a Hound
1. Add/adjust the hound in `Nodes/`, its `Config/*.json`, register it in `Program.cs`,
   and wire it into the graph + node dictionary.
2. Add MSTest unit tests in `Hound.Grocery.Tests`.
3. Add ≥5 eval scenarios under `Hound.Eval/Scenarios/{HoundName}/` (see the hound-eval
   skill) — evals land in the dedicated eval phase, but every shipped hound needs them.
4. Validate: `dotnet build src/Hound.sln` + `dotnet test src/Hound.sln`, and the
   sidecar's `pytest` suite for any `grocery-browser` change.
