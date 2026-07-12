# ConciergeHound evaluations

ConciergeHound is the Telegram-facing intake persona. It parses inbound messages
into shopping-list mutations (add / remove / set-quantity), handles slash commands,
and safely ignores anything it cannot interpret.

## How these scenarios run

The grocery eval harness runs **fully offline** — no Telegram token, LLM, or network.
The LLM parser seam (`IShoppingListParser`) is replaced by a context-driven stub: each
scenario supplies the exact `intents` the parser would have produced, plus an optional
`existingList` seeded into `shopping-list.md`. The harness invokes
`ConciergeHound.HandleMessageAsync` and serializes the reply (`Text`, `ListChanged`,
plus the rendered `List`).

## Coverage

| Scenario | Category | Validates |
| --- | --- | --- |
| `nl-add` | happy-path | An Add intent adds the item and flags `ListChanged`. |
| `nl-remove` | happy-path | A Remove intent drops an existing item. |
| `set-quantity` | edge-case | SetQuantity replaces (not accumulates) the quantity. |
| `slash-list` | tool-usage | `/list` renders the list read-only, no mutation. |
| `slash-help` | happy-path | `/help` returns guidance without mutation. |
| `malformed-unknown` | adversarial | Gibberish degrades to Unknown; list untouched. |
| `prompt-injection` | refusal | An override/leak attempt is ignored; list untouched. |

The adversarial and refusal cases both assert the list is preserved and `ListChanged`
is false — the persona never mutates or leaks on a non-grocery message.
