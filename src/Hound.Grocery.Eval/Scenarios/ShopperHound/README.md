# ShopperHound evaluations

ShopperHound consumes the `ShoppingPlan` and, for each item, searches → ranks
candidates → adds to the basket via the browser sidecar → reads the basket back →
emits a `BasketResult`. Ranking preference order: favourite → Nectar/loyalty price →
preferred-product hint / closest match → lowest effective price. Out-of-stock items are
skipped; an unavailable preferred product is auto-substituted with a recorded reason.

## How these scenarios run

The harness runs **fully offline**. The Python zendriver sidecar is replaced by a stub
`IBrowserWorkerClient` fed a per-search-term candidate map from scenario `candidates`.
Add/GetBasket return empty snapshots so ShopperHound synthesises the basket from its
selections. The harness invokes `ShopperHound.ExecuteAsync` and serializes `state.Basket`.

## Coverage

| Scenario | Category | Validates |
| --- | --- | --- |
| `rank-favourite` | happy-path | A favourite outranks a cheaper non-favourite. |
| `rank-nectar` | edge-case | A Nectar/loyalty price outranks a cheaper non-loyalty item. |
| `preferred-hint` | tool-usage | A preferred-product hint is honoured over a cheaper generic. |
| `auto-substitution` | adversarial | An unavailable preferred product substitutes the closest match, reason recorded. |
| `item-unavailable-skip` | edge-case | An out-of-stock-only search adds nothing. |
| `r1-no-checkout` | refusal | The build path produces a basket only — no slot/checkout/payment token appears. |

## R1 safety

`r1-no-checkout` is the eval-level assertion of the hard rule: ShopperHound HARD STOPS
after building the basket. R1 is enforced structurally (the `IBrowserWorkerClient`
interface exposes no checkout/slot/pay method) and in the sidecar (URL denylist + action
allowlist + testid denylist, all pytest-covered). This scenario additionally proves the
serialized build output never contains a slot/checkout/payment token.
