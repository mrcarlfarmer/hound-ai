# BudgetHound evaluations

BudgetHound reconciles a `BasketResult` against a pay-cycle anchored ledger and emits a
`BudgetVerdict`. The weekly budget has a ±10% flex band; the monthly/pay-cycle cap is a
**soft** cap — a human checks out, so BudgetHound never hard-blocks. It flags and asks
(via the Concierge) rather than refusing. Outcome enum: `Ok` / `WeeklyOver` /
`CycleOver` / `Both`.

## How these scenarios run

Budget math is **deterministic .NET** — no LLM. Each scenario supplies synthetic ledger
inputs (`basketSubtotal`, `priorWeekSpend`, `priorCycleSpend`, `weeklyTarget`,
`weeklyFlexPercent`, `monthlyCap`, `cycleStartDay`, optional `asOf`). The harness calls
`BudgetLedgerService.EvaluateBasket` (+ `CurrentWeekStart` when `asOf` is set) and
serializes `{verdict, currentWeekStart}`.

## Coverage

| Scenario | Category | Validates |
| --- | --- | --- |
| `within-flex-ok` | happy-path | Week inside the flex band → `Ok`, no approval. |
| `weekly-over` | edge-case | Week past the flex band → `WeeklyOver`, asks to approve/trim. |
| `cycle-over` | edge-case | Weekly-OK basket tips the cycle over cap → `CycleOver`, references monthly cap. |
| `both-over` | adversarial | Both weekly and cycle over → `Both`. |
| `cycle-start-boundary` | edge-case | Pay-cycle window rollover on the `CycleStartDay` boundary. |
| `soft-cap-flag-and-ask` | refusal | Over-cap basket flags + asks (RequiresApproval), never refuses. |

The soft-cap scenario is the R2/R3 assertion: even over budget, BudgetHound produces a
verdict + approval question — it never blocks the human from checking out.
