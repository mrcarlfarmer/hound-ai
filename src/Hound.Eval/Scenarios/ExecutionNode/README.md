# ExecutionHound Evaluation Scenarios

## Purpose

These scenarios validate that `ExecutionHound` correctly executes approved trades via the `place_market_order` tool, uses adjusted quantities from modified risk assessments, and refuses to execute rejected trades.

## Hound Overview

`ExecutionHound` receives a `RiskAssessment` (verdict, decision, reasoning, adjustedQuantity) and returns an `ExecutionResult` (success, symbol, action, quantity, filledPrice, orderId, message).

Execution rules:
- `Rejected` verdict → return `success=false` immediately without calling any tools
- `Approved` or `Modified` verdict → call `place_market_order` with effective quantity (use `adjustedQuantity` if set)

## Stub Behavior

The eval harness uses `StubAlpacaService.SubmitOrderAsync`, which:
- Returns a `StubOrder` with a generated `OrderId` and `OrderStatus.New`
- Accepts any symbol, quantity, and side

## Scenarios

| # | File | Category | Description |
|---|------|----------|-------------|
| 1 | `01_approved-market-order.json` | happy-path | Approved Buy AAPL 50 shares → success=true |
| 2 | `02_approved-limit-order.json` | happy-path | Approved Sell TSLA 30 shares → success=true |
| 3 | `03_partial-fill.json` | edge-case | Modified verdict with adjustedQuantity → uses adjusted qty |
| 4 | `04_tool-usage.json` | tool-usage | place_market_order called with correct NVDA parameters |
| 5 | `05_rejected-trade.json` | refusal | Rejected verdict → success=false, no order placed |

## Scoring

All scenarios use **binary** scoring based on keyword checks on the serialized `ExecutionResult` output.

## Running

```bash
dotnet run --project src/Hound.Eval -- --hound ExecutionHound --verbose
dotnet run --project src/Hound.Eval -- --hound ExecutionHound --dry-run
```
