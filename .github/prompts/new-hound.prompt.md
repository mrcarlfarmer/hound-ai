---
description: "Scaffold a new hound: class, config, DI registration, workflow integration, eval scenarios, and unit tests"
agent: "agent"
argument-hint: "HoundName and PackName, e.g. SentimentHound in trading-pack"
---
Create a new hound following all project conventions. Use the hound-eval skill for eval scenarios.

## Inputs
- **Hound name**: {{houndName}}
- **Pack**: {{packName}}
- **Purpose**: {{purpose}}

## Steps

1. **Hound class** in `src/Hound.{{packName}}/Hounds/{{houndName}}.cs`
   - Private `ChatClientAgent _agent` with instructions, name, description
   - Constructor: `IChatClient`, `IActivityLogger`, optional `ILoggerFactory?`, plus any domain services
   - Const `HoundId` (kebab-case) and `PackId`
   - Public async method returning a typed record
   - Log activity before and after agent invocation
   - Parse JSON response with `PropertyNameCaseInsensitive = true`
   - Add the response record to `HoundModels.cs` if not already there

2. **Config** in `src/Hound.{{packName}}/Config/{{houndName}}.json`

3. **DI registration** — add singleton in `src/Hound.{{packName}}/Program.cs` following existing pattern

4. **Workflow** — wire into the pack's workflow graph in `src/Hound.{{packName}}/Workflows/`

5. **Eval scenarios** — create ≥5 JSON scenarios in `src/Hound.Eval/Scenarios/{{houndName}}/`:
   - Categories: happy-path, edge-case, adversarial, tool-usage, refusal
   - Follow schema from existing scenarios (scenarioName, description, houndName, category, input, expectedBehavior, scoring)

6. **Unit tests** in `src/Hound.{{packName}}.Tests/` with MSTest + Moq

7. **Validate** — run `dotnet run --project src/Hound.Eval -- --dry-run` and `dotnet build src/Hound.sln`
