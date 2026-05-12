# Trading Strategy Blueprint

## Overview

The Trading Pack is a team of five specialised AI hounds that work together to analyse markets, formulate strategies, manage risk, and execute trades. Four hounds operate in a sequential pipeline triggered every four hours on weekdays, while a fifth hound runs independently to continuously improve the team's configuration.

The pack trades a configured watchlist of symbols (default: AAPL, MSFT, SPY) using Alpaca Markets paper trading.

---

## The Hound Team

### 1. Analysis Hound — Quantitative Market Analyst

**Role:** Fetches and analyses raw market data to produce a quantitative snapshot of each symbol.

**Responsibilities:**

- Retrieves 7 days of daily price bars (open/high/low/close/volume) from Alpaca
- Calculates price trend direction (Bullish, Bearish, or Neutral)
- Assigns a confidence score (0–1) based on trend strength using weighted indicators:
  - Price momentum (40%)
  - Volume change (30%)
  - Trend strength (30%)
- Produces a `MarketAnalysis` containing: symbol, last price, volume change, trend, confidence score, and summary

**Configuration:**

- Model: `qwen3` at temperature 0.2
- Lookback window: 7 days
- Confidence threshold: 0.5

---

### 2. Strategy Hound — Algorithmic Trading Strategist

**Role:** Receives the market analysis and decides whether to buy, sell, or hold.

**Responsibilities:**

- Evaluates trend direction and confidence score against configurable thresholds
- Decision logic:
  - **Buy** — Bullish trend with confidence ≥ 0.7
  - **Sell** — Bearish trend with confidence ≥ 0.7
  - **Hold** — All other cases
- Produces a `TradingDecision` containing: symbol, action, quantity, reasoning, and confidence
- Considers technical indicators (RSI, MACD, SMA20) across 1-day and 4-hour timeframes

**Configuration:**

- Model: `qwen3` at temperature 0.1
- Bullish confidence threshold: 0.7
- Bearish confidence threshold: 0.7
- Entry threshold: 0.7
- Exit threshold: 0.6

---

### 3. Risk Hound — Risk Management Specialist

**Role:** Reviews proposed trades against portfolio rules and approves, rejects, or modifies them.

**Responsibilities:**

- Retrieves current portfolio state (equity and open positions) from Alpaca
- Enforces three hard risk limits:
  - **Single-position cap:** No single position may exceed 20% of portfolio equity
  - **Total exposure cap:** Total equity exposure must not exceed 80% of portfolio
  - **Per-order share limit:** No single order may exceed 1,000 shares
- Issues a verdict:
  - **Approved** — Trade passes all risk checks
  - **Modified** — Trade is viable but quantity is reduced to fit within limits
  - **Rejected** — Trade cannot be made within risk constraints
- Produces a `RiskAssessment` containing: verdict, original decision, reasoning, and adjusted quantity (if modified)

**Configuration:**

- Model: `qwen3` at temperature 0.1
- Max position: 20% of equity
- Max exposure: 80% of equity
- Max shares per order: 1,000
- Max drawdown: 10%

---

### 4. Execution Hound — Execution Trader

**Role:** Places approved orders on Alpaca and manages order lifecycle.

**Responsibilities:**

- Creates a `TradeDocument` in the database with Pending status before placing any order
- Submits market orders to Alpaca
- Can check order status and cancel orders if needed
- Manages the order lifecycle: Pending → Filled / Canceled / Expired
- Returns an `ExecutionResult` containing: success flag, symbol, action, quantity, filled price, order ID, and trade document ID

**Configuration:**

- Model: `qwen3.5:9b` at temperature 0.0 (fully deterministic)
- Order type: Market
- Slippage tolerance: 0.1%
- Time-in-force: Day
- Order watch interval: 5 seconds
- Order watch timeout: 30 minutes

---

### 5. Tuner Hound — Autonomous Configuration Tuner

**Role:** Proposes incremental configuration improvements for the other hounds. Runs independently from the main trading pipeline on a 30-minute cycle.

**Responsibilities:**

- Rotates through hounds in round-robin order (Strategy → Risk → Analysis → Execution)
- Reviews each hound's current configuration and recent experiment history
- Proposes exactly one field modification per experiment, making conservative adjustments
- Scores configurations heuristically (0–1) with domain-specific scoring per hound:
  - **Strategy Hound:** Penalises high temperature and out-of-range thresholds
  - **Risk Hound:** Favours conservative limits, penalises high position/drawdown percentages
  - **Analysis Hound:** Penalises very short (< 3 day) or very long (> 30 day) windows
  - **Execution Hound:** Penalises high slippage tolerance and temperature
- Records each experiment with before/after config, scores, delta, and rationale
- Assigns a status based on the score delta:
  - **PendingReview** (delta > 0.001) — Improvement found, awaiting human approval
  - **Worse** (delta < -0.001) — Rejected automatically
  - **Equal** — No significant change
- **Never auto-applies changes** — all improvements require human review
- Never modifies the Model field
- Avoids re-proposing changes previously marked as worse

**Tunable Fields:**

| Hound | Allowed Fields |
|---|---|
| Strategy Hound | BullishConfidenceThreshold, BearishConfidenceThreshold, EntryThreshold, ExitThreshold, Temperature, Instructions |
| Risk Hound | MaxPositionPct, MaxDrawdownPct, MaxSharesPerOrder, Temperature, Instructions |
| Analysis Hound | DataWindowDays, ConfidenceThreshold, Temperature, Instructions |
| Execution Hound | SlippageTolerance, Temperature, Instructions |

---

## Supporting Services

### Order Watcher Service

A background service that continuously monitors pending orders and updates their status.

- Polls Alpaca every 5 seconds for status changes on pending or partially filled orders
- Updates trade documents in the database with fill status, filled quantity, and average fill price
- Times out stale orders after 30 minutes, marking them as Expired
- Runs independently from the main pipeline — never blocks trading

### Tuner Hosted Service

A background service that drives the Tuner Hound on a scheduled cycle.

- Runs every 30 minutes
- Can be paused and resumed (supports starting in a paused state)
- Logs experiment results for dashboard visibility
- Operates independently — never blocks the main trading pipeline

---

## Trading Pipeline

The pipeline runs every 4 hours on weekdays (cron: `0 */4 * * 1-5`) and processes each symbol sequentially.

```
For each symbol in watchlist:

  ┌─────────────────┐
  │  Analysis Hound │  Fetch 7 days of market data → MarketAnalysis
  └────────┬────────┘
           │
           ▼
     Confidence ≥ 0.5?
      No → Skip symbol
      Yes ↓
           │
  ┌─────────────────┐
  │  Strategy Hound │  Evaluate trend + confidence → TradingDecision
  └────────┬────────┘
           │
           ▼
     Action == Hold?
      Yes → Skip symbol
      No  ↓
           │
  ┌─────────────────┐
  │    Risk Hound   │  Check portfolio constraints → RiskAssessment
  └────────┬────────┘
           │
           ▼
     Verdict == Rejected?
      Yes → Skip symbol
      No  ↓
           │
     (Optional human-in-the-loop approval gate)
           │
           ▼
  ┌─────────────────┐
  │ Execution Hound │  Place market order → ExecutionResult
  └────────┬────────┘
           │
           ▼
     Order placed → OrderWatcherService monitors fill status
```

### Early Exit Gates

The pipeline has multiple points where it stops processing a symbol early:

1. **Low confidence** — Analysis confidence below 0.5 (configurable)
2. **Hold decision** — Strategy Hound determines no action is warranted
3. **Risk rejection** — Risk Hound rejects the trade due to portfolio constraints
4. **Human-in-the-loop** — Optional manual approval gate after risk assessment

### Data Flow

Each hound produces a structured record that feeds into the next:

```
MarketAnalysis → TradingDecision → RiskAssessment → ExecutionResult
```

- `MarketAnalysis` — Raw market facts (price, volume, trend, confidence)
- `TradingDecision` — Strategic choice with reasoning (action, quantity, confidence)
- `RiskAssessment` — Risk verdict wrapping the original decision (approved/modified/rejected)
- `ExecutionResult` — Order outcome with trade document reference for lifecycle tracking

---

## Trading Strategy Summary

The pack follows a **momentum-based trading strategy** with conservative risk management:

1. **Analyse** — Quantify market momentum using price action and volume over a 7-day window
2. **Decide** — Only trade when confidence is high (≥ 0.7) and trend is clearly directional
3. **Guard** — Enforce strict portfolio risk limits before any order is placed
4. **Execute** — Place market orders with deterministic execution and full lifecycle tracking
5. **Improve** — Continuously propose and evaluate configuration changes, but always require human approval

The strategy is deliberately conservative: it holds rather than trades when signals are uncertain, caps position sizes to limit exposure, and uses low LLM temperatures throughout the pipeline to favour consistency over creativity.

---

## Default Watchlist

| Symbol | Description |
|---|---|
| AAPL | Apple Inc. |
| MSFT | Microsoft Corporation |
| SPY | S&P 500 ETF |
