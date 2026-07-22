# AnalysisHound Evaluation Scenarios

## Purpose

These scenarios validate that `AnalysisHound` correctly analyses market data via its `fetch_market_data` tool, produces appropriate trend assessments with confidence scores, gracefully handles missing data, and stays within its market-analysis scope.

## Hound Overview

`AnalysisHound` receives a stock symbol and returns a `MarketAnalysis` containing:
- `symbol`, `lastPrice`, `volumeChange`
- `trend`: `Bullish` | `Bearish` | `Neutral` | `Unknown`
- `confidenceScore`: 0.0–1.0
- `summary`: human-readable analysis

It uses the `fetch_market_data` tool to retrieve 7 days of historical bars from Alpaca Markets.

## Stub Behavior

The eval harness uses `StubAlpacaService`:
- **Normal symbols**: Returns 3 days of ascending bars (148→154→158 close prices).
- **Symbols containing `NODATA`**: Returns empty bar list to trigger the insufficient-data path.

It also uses `StubNewsService` and `StubSentimentService`:
- **`StubNewsService`**: Returns a single synthetic headline for any symbol, unless the symbol contains `NONEWS` (returns an empty list).
- **`StubSentimentService`**: Returns a bullish snapshot for symbols containing `BULL`, a bearish snapshot for `BEAR`, and an empty snapshot otherwise.

## Scenarios

| # | File | Category | Description |
|---|------|----------|-------------|
| 1 | `01_uptrend.json` | happy-path | AAPL with rising bars → Bullish, high confidence |
| 2 | `02_sideways-market.json` | happy-path | MSFT with modest movement → valid analysis |
| 3 | `03_insufficient-data.json` | edge-case | No bar data → acknowledges limitation |
| 4 | `04_historical-bars.json` | tool-usage | Must call fetch_market_data with correct symbol |
| 5 | `05_non-market-analysis.json` | refusal | Cooking recipe injected → stays on market analysis |
| 6 | `06_news-and-sentiment-present.json` | happy-path | News + bullish sentiment available → analysis cites symbol |
| 7 | `07_no-news-graceful.json` | edge-case | NONEWS symbol → no fabricated headlines |
| 8 | `08_bearish-sentiment.json` | happy-path | BEAR symbol → bearish sentiment reflected |

## Scoring

All scenarios use **binary** scoring based on keyword checks on the serialized `MarketAnalysis` output.

## Running

```bash
dotnet run --project src/Hound.Eval -- --hound AnalysisHound --verbose
dotnet run --project src/Hound.Eval -- --hound AnalysisHound --dry-run
```
