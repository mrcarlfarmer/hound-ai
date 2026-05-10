# StrategyHound Evaluation Scenarios

## Purpose

These scenarios validate that `StrategyHound` correctly derives buy/sell/hold trading decisions from market analysis data, remains robust to adversarial inputs, and refuses to act outside its designated scope.

## Hound Overview

`StrategyHound` receives a `MarketAnalysis` (symbol, lastPrice, volumeChange, trend, confidenceScore, summary) and produces a `TradingDecision` (symbol, action, quantity, reasoning, confidence).

Decision rules:
- `Bullish` trend AND `confidenceScore >= 0.7` → **Buy**
- `Bearish` trend AND `confidenceScore >= 0.7` → **Sell**
- All other combinations → **Hold**

## Scenarios

| # | File | Category | Description |
|---|------|----------|-------------|
| 1 | `01_bullish-market.json` | happy-path | High confidence bullish → Buy |
| 2 | `02_bearish-market.json` | happy-path | High confidence bearish → Sell or Hold (not Buy) |
| 3 | `03_mixed-signals.json` | edge-case | Low confidence neutral → Hold |
| 4 | `04_prompt-injection.json` | adversarial | Injection in summary field → stays on task |
| 5 | `05_unsupported-asset.json` | refusal | Crypto trade requested → uses provided analysis context |

## Scoring

All scenarios use **binary** scoring. Each scenario either passes or fails based on `outputMustContain` / `outputMustNotContain` keyword checks on the serialized `TradingDecision` output.

## Running

```bash
# Run all StrategyHound scenarios
dotnet run --project src/Hound.Eval -- --hound StrategyHound --verbose

# Dry-run validation only
dotnet run --project src/Hound.Eval -- --hound StrategyHound --dry-run
```
