---
description: "Use when writing or editing C# code in the Hound AI platform. Covers AF agent patterns, activity logging, DI conventions, and hound class structure."
applyTo: "src/**/*.cs"
---
# Hound AI — C# Conventions

## Agent Framework Patterns
- Hounds wrap `ChatClientAgent` from `Microsoft.Agents.AI` — keep as a private field
- Define tools via `AIFunctionFactory.Create(...)` passed to the agent constructor
- Run agents with `_agent.RunAsync(messages, options, cancellationToken)` — fresh session per call
- Parse responses with `JsonSerializer.Deserialize<T>(response.Text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })`

## Activity Logging
- All hounds log activity via `IActivityLogger.LogActivityAsync()` **before and after** agent invocation
- Never use `Console.WriteLine` — use `IActivityLogger` for hound activity, `ILogger<T>` for infrastructure

## DI Registration
- Hounds registered as **singletons** in the pack's `Program.cs`
- Per-hound model configured via `builder.Configuration["Hounds:{Name}:Model"]`
- Use `IOllamaClientFactory` → cast to `OllamaClientFactory` to call `CreateChatClient(model)`
- Settings bound via `IOptions<T>` — config from `Config/*.json` and environment variables

## Hound Class Structure
- Const `HoundId` (kebab-case: `analysis-hound`) and `PackId` (kebab-case: `trading-pack`)
- Constructor: `IChatClient`, `IActivityLogger`, optional `ILoggerFactory?`, plus domain services
- Public async method returns a typed **record** (defined in shared models)
- `CancellationToken` on all public async methods

## Workflows
- Hound outputs feed into downstream hounds via typed records (e.g., `MarketAnalysis` → `TradingDecision`)
- Workflows are sequential chains orchestrated in `Workflows/` classes
- Confidence thresholds and symbols driven by `IOptions<TSettings>`

## Nullable Reference Types
- Declare variables non-nullable, and check for `null` at entry points
- Always use `is null` or `is not null` instead of `== null` or `!= null`
- Trust the C# null annotations — don't add null checks when the type system says a value cannot be null

## Data Access
- RavenDB document store — no Entity Framework
- Custom indexes in `Indexes/` classes extending `AbstractIndexCreationTask`
- Repositories implement interfaces (`IPackRepository`, `ITunerExperimentRepository`, etc.)

## Style
- Use `nameof` instead of string literals when referring to member names
- XML doc comments on public APIs — include `<example>` and `<code>` when applicable
- Do not emit "Act", "Arrange" or "Assert" comments in tests
- Copy existing style in nearby files for test method names and capitalization
