# LearnerHound evaluations

LearnerHound is the writer side of the learning loop that PlannerHound's
`PreferenceService` reads. It learns from purchase observations (and dislikes),
refining `preferences.md`: product mappings, typical quantities, favourite/staple
promotion, dislikes, and a confidence score that rises with consistent observations.
It is **read-merge-write** — existing human-curated and learned entries are never
clobbered — via the single shared `PreferencesSerializer`.

## How these scenarios run

Learning is **deterministic .NET** — no LLM. Each scenario supplies synthetic
`observations` (`item`, `chosenProduct`, `quantity`, `date`), optional `dislikes`, and
an optional `existingPreferences` markdown written to `preferences.md`. The harness runs
`PreferenceLearner.Learn`, renders the result through `PreferencesSerializer`, and
serializes `{Changes, Preferences}`.

## Coverage

| Scenario | Category | Validates |
| --- | --- | --- |
| `first-time-learn` | happy-path | New item learned with product, usual qty, starting confidence. |
| `reinforcement` | happy-path | Consistent repeats raise confidence. |
| `favourite-promotion` | edge-case | Enough purchases promote an item to favourite. |
| `staple-promotion` | edge-case | Enough distinct dates promote an item to staple. |
| `dislike-clears-mapping` | refusal | A dislike matching the preferred product clears it to `(none)`. |
| `trusted-not-overwritten` | adversarial | A trusted mapping survives a little divergent evidence. |

`trusted-not-overwritten` proves the read-merge-write protection: a high-confidence
mapping is not clobbered by a couple of divergent purchases, so the learning loop
degrades gracefully rather than thrashing.
