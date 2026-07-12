# PlannerHound evaluations

PlannerHound turns the loose `shopping-list.md` into a concrete `ShoppingPlan` —
per item: raw text, search term, preferred-product hint, target quantity, priority,
and favourite/staple flags. Resolution order is exact learned mapping → fuzzy
embedding match → cold-start LLM suggestion.

## How these scenarios run

The harness runs **fully offline**. The two non-deterministic seams are stubbed from
scenario context: `IItemMatcher` (embedding fuzzy match) is fed a canned `match`
`{candidate, score}`, and `IPlannerAssistant` (cold-start LLM) is fed a canned
`suggestion` `{searchTerm, productHint, quantity}`. An optional `preferences` string is
written to `preferences.md`, and `weeklyTarget` threads the loose budget context. The
harness invokes `PlannerHound.ExecuteAsync` and serializes `state.Plan`.

## Coverage

| Scenario | Category | Validates |
| --- | --- | --- |
| `exact-preference` | happy-path | A confident learned mapping resolves directly; favourite → High priority. |
| `fuzzy-match` | edge-case | A synonym with no exact mapping fuzzy-matches an existing key. |
| `cold-start-llm-hint` | happy-path | Empty preferences → assistant search term + product hint, Normal priority. |
| `favourite-staple-high` | tool-usage | Favourite/staple learned item plans at High priority. |
| `weekly-budget-threading` | happy-path | Configured weekly target is carried onto the plan. |
| `coldstart-graceful` | adversarial | Unknown item with no prefs/hint falls back to the raw item, never crashes. |

**Cold-start note:** LearnerHound normally populates `preferences.md`, but on a fresh
system it is empty. The planner must degrade to the assistant/raw-item fallback rather
than fail — the two cold-start scenarios prove that.
