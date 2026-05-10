---
description: "Use when editing eval harness code, scenarios, or adding new hound evaluations. Covers scenario JSON schema, stub services, and the eval runner dispatch pattern."
applyTo: "src/Hound.Eval/**"
---
# Eval Harness Conventions

## Architecture
- `Program.cs` — CLI entry point; parses `--hound`, `--category`, `--verbose`, `--dry-run` flags
- `EvalRunner` — loads scenarios from `Scenarios/{HoundName}/`, dispatches to hounds, collects results
- `JsonEvalScenario` — deserializes scenario JSON files implementing `IEvalScenario`
- `EvalReport` — aggregates pass/fail counts and per-scenario details

## Stub Services
- `NullActivityLogger` — no-op `IActivityLogger` so hounds run without RavenDB
- `StubAlpacaService` — returns canned market data and portfolio responses; no live API calls
- Both registered via DI in the eval harness `Program.cs`

## Scenario Structure
Each scenario is a JSON file in `Scenarios/{HoundName}/`:
```
Scenarios/
  AnalysisHound/
    happy-path-bullish-trend.json
    edge-case-flat-market.json
    adversarial-malformed-data.json
    tool-usage-fetch-bars.json
    refusal-invalid-symbol.json
    README.md
```

Required categories (minimum 5 per hound): happy-path, edge-case, adversarial, tool-usage, refusal.

## Dry-Run Mode
`--dry-run` validates scenario JSON parsing without invoking any LLM — use this in CI and after adding scenarios.
