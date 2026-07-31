# Spec-created card metadata (Trello description)

When `monkify-create-spec` finishes, the card description **must** include an **`## Implementation`**
section with fixed field names. `monkify-ship-feature` reads branch and spec path from here — do not
rename the labels.

Append or replace this block at the end of the card description (keep Problem, Decisions, etc.
above). Update **`## Status`** to `Spec created — ready for implementation`.

```markdown
## Implementation
- **Worktree:** `.worktrees/<slug>` (per repo → `.worktrees/<slug>/<repo>`, under the workspace-root `.worktrees/` dir)
- **Branch:** `feature/<slug>`
- **Spec:** `monkify-meta/specs/<slug>.md`
- **Repos:** monkify-backend, monkify-front, monkify-meta
- **Level:** Feature
- **Release branch:** `release/orbit`   ← OPTIONAL; omit entirely for a normal ship

## Status
Spec created — ready for implementation.
```

The **Worktree** is the primary artifact — where the in-flight work physically lives. The **Branch**
is the merge/PR ref (still required; ship-feature merges it into `main`).

## Rules

| Field | Format |
| --- | --- |
| **Worktree** | **Required, primary.** Base path of the isolated worktree(s), in backticks: `.worktrees/<slug>` (each repo checks out at `.worktrees/<slug>/<repo>`). See below. |
| **Branch** | **Required.** Same branch name in every listed repo: `feature/<slug>` in backticks (the merge/PR ref) |
| **Spec** | Full path from workspace root: `monkify-meta/specs/<slug>.md` in backticks |
| **Repos** | Comma-separated repo folder names (include `monkify-meta` when spec lives there) |
| **Level** | **Required.** `Feature` or `Minor change`, copied from the refined card's `## Level` section. Gates the downstream **feature artifacts**: `Feature` → mandatory happy-path integration test (`monkify-implement-feature`) **and** a dedicated `features/<slug>.md` doc (`monkify-ship-feature`); `Minor change` → both waived (unit tests + existing-doc updates still apply). If the refined card has no `## Level`, default to **`Feature`** (never skip an artifact by guessing). |

The slug in **Spec** and **Branch** must match (`free-bet-mode` → `feature/free-bet-mode` +
`specs/free-bet-mode.md`).

## Parsing (for ship-feature)

Extract with regex or line match:

- `**Worktree:**` → base path inside backticks (each repo at `<base>/<repo>`); the registered
  location of the in-flight work
- `**Branch:**` → branch inside backticks (the merge/PR ref)
- `**Spec:**` → path inside backticks
- `**Repos:**` → remainder of line (split on commas, trim)
- `**Release branch:**` → branch inside backticks, **optional**. Present ⇒ this card rides a RELEASE
  TRAIN: `monkify-ship-feature` merges the feature into that branch instead of `main`, and leaves the
  card in **Implemented** (its code is not on `main`, so Shipped would be false). The train is landed
  separately with `/monkify-ship-feature <release branch>`, which merges it to `main` and ships every
  card naming it. **Absent is the normal case** and means merge straight to `main`.
- `**Level:**` → `Feature` or `Minor change` (case-insensitive; anything not clearly `Minor change`
  is treated as `Feature`). If the line is absent (older cards), default to `Feature`.

If **Implementation** or **Branch** is missing, stop and tell the user to run `/monkify-create-spec`
again or add the block manually. (Older cards may lack **Worktree:** — that's tolerated; ship-feature
falls back to creating its own ship worktree off `main`.)

## Worktree registration (all Monkify skills)

Per `AGENTS.md` rule 6, every Monkify skill does its repo work in an **isolated git worktree** (one
per affected repo, with the shared checkout left on `main` as a hub) so concurrent agents never
collide. Whenever a skill **creates or enters** a worktree, it must **register that worktree on the
card** so anyone (human or agent) looking at the card sees where the in-flight work lives:

- **Persistent pointer** — `monkify-create-spec` writes the `**Worktree:**` line in the
  `## Implementation` block above (the current location of the feature's work). Use the shared base
  `.worktrees/<slug>`; each repo checks out at `.worktrees/<slug>/<repo>`, created under the
  workspace-root `.worktrees/` dir. If the worktrees sit at different paths per repo, list each:
  `monkify-backend → <path>; monkify-meta → <path>`.
- **Registration comment** — every skill that creates/enters a worktree adds a Trello comment when it
  does so:

  ```markdown
  🌳 Worktree registered (YYYY-MM-DD) — <repo> `feature/<slug>` → `<worktree-path>`
  ```

  One comment may cover all affected repos (one line each). This is the audit trail of "who is
  working where."
- **Retire on ship** — `monkify-ship-feature`, after merging to `main`, adds a
  `🌳 Worktree retired (YYYY-MM-DD)` comment (the branch is merged, so the worktree can be removed).

Keep the `**Worktree:**` field and the branch in sync: if the worktree moves, update both.
