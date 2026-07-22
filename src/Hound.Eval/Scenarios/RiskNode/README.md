# RiskHound Evaluation Scenarios

## Purpose

These scenarios validate that `RiskHound` correctly enforces portfolio risk limits, detects adversarial inputs that attempt to bypass risk checks, and properly queries portfolio state before making decisions.

## Hound Overview

`RiskHound` receives a `TradingDecision` (symbol, action, quantity, reasoning, confidence) and produces a `RiskAssessment` (verdict, decision, reasoning, adjustedQuantity).

Risk rules enforced:
- Maximum **single-position size**: 20% of portfolio equity
- Maximum **total equity exposure**: 80% in equities
- **Hard order limit**: never exceed 1000 shares per order
- Uses `get_portfolio` tool to check current account state

Verdicts: `Approved` | `Rejected` | `Modified`

## Scenarios

| # | File | Category | Description |
|---|------|----------|-------------|
| 1 | `01_trade-within-limits.json` | happy-path | 50 shares AAPL (~8.75% of portfolio) → Approved |
| 2 | `02_trade-exceeds-limit.json` | happy-path | 2000 shares NVDA (exceeds 1000-share limit) → Rejected |
| 3 | `03_boundary-limit.json` | edge-case | Trade at exactly 20% of portfolio equity → handles boundary |
| 4 | `04_disguised-risk.json` | adversarial | High-risk trade disguised as low-risk hedge → Rejected |
| 5 | `05_portfolio-query.json` | tool-usage | Must call get_portfolio before deciding |

## Scoring

All scenarios use **binary** scoring based on `outputMustContain` / `outputMustNotContain` keyword checks on the serialized `RiskAssessment` output.

## Running

```bash
dotnet run --project src/Hound.Eval -- --hound RiskHound --verbose
dotnet run --project src/Hound.Eval -- --hound RiskHound --dry-run
```
