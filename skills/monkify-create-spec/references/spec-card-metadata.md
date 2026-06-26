# Spec-created card metadata (Trello description)

When `monkify-create-spec` finishes, the card description **must** include an **`## Implementation`**
section with fixed field names. `monkify-ship-feature` reads branch and spec path from here — do not
rename the labels.

Append or replace this block at the end of the card description (keep Problem, Decisions, etc.
above). Update **`## Status`** to `Spec created — ready for implementation`.

```markdown
## Implementation
- **Spec:** `monkify-meta/specs/<slug>.md`
- **Branch:** `feature/<slug>`
- **Repos:** monkify-backend, monkify-front, monkify-meta

## Status
Spec created — ready for implementation.
```

## Rules

| Field | Format |
| --- | --- |
| **Spec** | Full path from workspace root: `monkify-meta/specs/<slug>.md` in backticks |
| **Branch** | Same branch name in every listed repo: `feature/<slug>` in backticks |
| **Repos** | Comma-separated repo folder names (include `monkify-meta` when spec lives there) |

The slug in **Spec** and **Branch** must match (`free-bet-mode` → `feature/free-bet-mode` +
`specs/free-bet-mode.md`).

## Parsing (for ship-feature)

Extract with regex or line match:

- `**Spec:**` → path inside backticks
- `**Branch:**` → branch inside backticks
- `**Repos:**` → remainder of line (split on commas, trim)

If **Implementation** or **Branch** is missing, stop and tell the user to run `/monkify-create-spec`
again or add the block manually.
