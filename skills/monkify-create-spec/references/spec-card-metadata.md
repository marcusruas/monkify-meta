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
- **Worktree:** `.worktrees/feature-<slug>` (per repo, under the workspace-root `.worktrees/` dir)

## Status
Spec created — ready for implementation.
```

## Rules

| Field | Format |
| --- | --- |
| **Spec** | Full path from workspace root: `monkify-meta/specs/<slug>.md` in backticks |
| **Branch** | Same branch name in every listed repo: `feature/<slug>` in backticks |
| **Repos** | Comma-separated repo folder names (include `monkify-meta` when spec lives there) |
| **Worktree** | Path of the isolated git worktree the feature work lives in (see below), in backticks |

The slug in **Spec** and **Branch** must match (`free-bet-mode` → `feature/free-bet-mode` +
`specs/free-bet-mode.md`).

## Parsing (for ship-feature)

Extract with regex or line match:

- `**Spec:**` → path inside backticks
- `**Branch:**` → branch inside backticks
- `**Repos:**` → remainder of line (split on commas, trim)
- `**Worktree:**` → path inside backticks (optional — informational; ship-feature merges from the
  registered worktree when present, otherwise from the repo's normal checkout)

If **Implementation** or **Branch** is missing, stop and tell the user to run `/monkify-create-spec`
again or add the block manually.

## Worktree registration (all Monkify skills)

Per `AGENTS.md` must-follow rule 6, every Monkify skill does its repo work in an **isolated git
worktree** (one per affected repo) so concurrent agents never collide. Whenever a skill **creates or
enters** a worktree, it must **register that worktree on the card** so anyone (human or agent)
looking at the card sees where the in-flight work lives:

- **Persistent pointer** — `monkify-create-spec` writes the `**Worktree:**` line in the
  `## Implementation` block above (the current location of the feature's work). Use the worktree
  directory name shared across the affected repos, e.g. `.worktrees/feature-<slug>` — created per
  repo under the workspace-root `.worktrees/` dir (or a repo-local ignored `.worktrees/`). If the
  worktrees sit at different paths per repo, list each: `monkify-backend → <path>; monkify-meta → <path>`.
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
