---
name: hound-eval
description: "Ensure agent evaluations are created alongside every new hound. Use when: creating a new hound/agent, adding an agent to a pack, implementing a new AF Agent class. Triggers on: new hound, new agent, add agent, create agent, evaluation, eval."
---

# Hound Agent Evaluation Skill

## Purpose

Every hound (AI agent) in the Hound AI platform MUST have a corresponding set of evaluation scenarios. This skill ensures evaluations are always created when a new hound is created or modified.

## When to Use

- Creating a new hound class (any class using `AIAgent`, `ChatClientAgent`, or AF agent patterns)
- Adding a hound to an existing or new pack
- Significantly changing a hound's instructions, tools, or responsibilities

## Evaluation Pattern

Each hound's evaluations live in `src/Hound.Eval/Scenarios/{HoundName}/` as JSON files.

### Scenario File Format

Each JSON file in the scenarios folder follows this structure:

```json
{
  "scenarioName": "Descriptive name of the test scenario",
  "description": "What this scenario validates",
  "input": {
    "userMessage": "The input message/context sent to the hound",
    "context": {
    }
  },
  "expectedBehavior": {
    "shouldCallTools": ["tool_name_1", "tool_name_2"],
    "outputMustContain": ["keyword1", "keyword2"],
    "outputMustNotContain": ["forbidden_term"],
    "outputFormat": "Description of expected output structure",
    "decisionCriteria": "Description of what correct decision looks like"
  },
  "scoring": {
    "type": "binary | rubric",
    "passCriteria": "Description of what constitutes a pass",
    "rubric": [
      { "criterion": "Correctness", "weight": 0.4, "description": "Did the hound make the right decision?" },
      { "criterion": "Reasoning", "weight": 0.3, "description": "Did the hound explain its reasoning?" },
      { "criterion": "Tool Usage", "weight": 0.3, "description": "Did the hound use the correct tools with correct parameters?" }
    ]
  }
}
```

### Minimum Scenarios Per Hound

Every hound MUST have at least these scenario categories:

1. **Happy path** — Standard input where the hound should succeed normally
2. **Edge case** — Boundary conditions, unusual but valid inputs
3. **Adversarial** — Inputs designed to test robustness (malformed data, conflicting signals, prompt injection attempts)
4. **Tool usage** — Verify the hound calls the correct tools with correct parameters
5. **Refusal** — Scenarios where the hound should refuse to act or escalate

### Procedure When Creating a New Hound

1. Create the hound class in its pack directory (e.g., `src/Hound.Trading/Hounds/NewHound.cs`)
2. **Immediately** create the eval scenarios directory: `src/Hound.Eval/Scenarios/{NewHoundName}/`
3. Create at least 5 scenario JSON files covering all required categories above
4. Register the new hound's scenarios in the eval runner if needed
5. Run `dotnet run --project src/Hound.Eval -- --hound {NewHoundName}` to validate scenarios parse correctly
6. Document the hound's evaluation criteria in a `README.md` within its scenarios folder

### Eval Runner Usage

```bash
# Run all evaluations
dotnet run --project src/Hound.Eval

# Run evaluations for a specific hound
dotnet run --project src/Hound.Eval -- --hound StrategyHound

# Run evaluations and output detailed report
dotnet run --project src/Hound.Eval -- --verbose

# Run evaluations for a specific scenario category
dotnet run --project src/Hound.Eval -- --category adversarial
```

### Checklist

Before any PR introducing a new hound can be considered complete:

- [ ] Hound class created with clear instructions and tool definitions
- [ ] `src/Hound.Eval/Scenarios/{HoundName}/` directory exists
- [ ] At least 5 scenarios covering: happy path, edge case, adversarial, tool usage, refusal
- [ ] Each scenario file is valid JSON matching the schema above
- [ ] Scenarios run without parse errors
- [ ] `README.md` in the scenarios folder documents evaluation intent
