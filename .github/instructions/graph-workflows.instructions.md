---
description: Use when editing graph nodes, trading graph state, routing logic, or state store implementations. Covers INode interface, graph topology, checkpoint/resume, and extension patterns.
applyTo: src/Hound.Trading/{Graph,Nodes}/**
---

# Graph Workflow System

## Core Abstractions

| Interface | Purpose | Location |
|-----------|---------|----------|
| `INode` | Single pipeline step — executes and returns updated state | `Graph/INode.cs` |
| `IStateStore` | Checkpoint persistence for resume across restarts | `Graph/IStateStore.cs` |
| `IResettableExecutor` | Resets Ollama KV cache between monitor cycles | `Graph/OllamaResettableExecutor.cs` |
| `GraphRunPublisher` | Broadcasts state to API via HTTP + persists `GraphRun` documents | `Graph/GraphRunPublisher.cs` |
| `TradingGraphState` | Immutable record flowing through all nodes — use `state with { ... }` | `Graph/TradingGraphState.cs` |

## Graph Topology (Route method)

```
Entry phase:
  null → analysts-team-node → strategy-node → risk-node → execution-node → monitor-node
  (risk-rejection loop: risk-node → strategy-node, up to MaxRefinements)
  (low confidence or hold: early exit to __end__)

Monitor phase:
  monitor-node ↔ analysts-team-node (cyclic until trade closes)
```

Routing is encoded in `TradingGraph.Route()` via pattern matching on `(Phase, CurrentNode)`.

## Implementing a New Node

1. Create class in `Nodes/` implementing `INode`
2. Set `NodeId` to a kebab-case identifier (e.g., `"my-node"`)
3. Set `PackId` to the owning pack (e.g., `"trading-pack"`)
4. Inject `IChatClient`, `IActivityLogger`, and any services via constructor
5. In `ExecuteAsync`: log activity → invoke agent → parse response → return `state with { ... }`
6. Register as singleton in `Program.cs` with keyed `IChatClient`
7. Add to the node dictionary in `Program.cs`
8. Update `TradingGraph.Route()` to include the new node in the topology
9. Add output slot to `TradingGraphState` if the node produces new data

## Node Conventions

- Nodes are **stateless singletons** — all mutable data lives in `TradingGraphState`
- Use `ChatClientAgent` from `Microsoft.Agents.AI` for LLM calls
- Always log via `IActivityLogger` before and after agent invocation
- Return typed records (defined in `Nodes/NodeModels.cs`) as output slots
- Handle JSON parsing failures gracefully — set `ErrorMessage` on state, don't throw

## State Immutability

`TradingGraphState` is a `record` — never mutate, always use `with` expressions:
```csharp
state = state with { StrategyOutput = decision, CurrentNode = NodeId };
```

## Keyed IChatClient

- `"strategy"` key → larger model (qwen3:14b) for complex reasoning
- `"default"` key → smaller model (qwen3.5:9b) for standard tasks
- Configured in `Program.cs`; model names from `appsettings.json` `Ollama:StrategyModel` / `Ollama:DefaultModel`

## Checkpoint & Resume

- `IStateStore.SaveAsync()` called after each node transition
- `TradingGraph.ResumeAsync(runId)` reloads state and continues from last checkpoint
- `ClearAsync()` on successful completion — don't leave stale checkpoints
