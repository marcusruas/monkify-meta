---
name: monkify-ship-feature
description: Merge a Monkify feature branch into main and sync the monkify-meta knowledge base from the spec, linked Trello task, and merged code. Use when the user wants to ship, land, or merge a feature. Takes a Trello card name on the monkaify board; reads branch and spec path from the card's Implementation section. Moves the card to Implemented when done.
disable-model-invocation: true
---

# Monkify — Ship Feature

Merge a completed feature into `main` in every affected repo, then update the **monkify-meta**
knowledge base so it describes how Monkify actually works after the merge — not how it worked
before.

This skill **merges code and rewrites docs**; it does not implement features.

## Input (required)

The user names the **Trello card** (exact title or unambiguous substring), e.g.:

> `/monkify-ship-feature Add Free bet mode`

**Branch** and **spec file** are **not** asked separately — read them from the card's
**`## Implementation`** section (written by `/monkify-create-spec`). See
`../monkify-create-spec/references/spec-card-metadata.md`.

If the card has no **Implementation** block or **Branch** line, **stop** and tell the user to run
`/monkify-create-spec` or add the block manually.

## Prerequisites (verify FIRST — stop if missing)

1. **Sibling repos** — `monkify-backend` and `monkify-front` exist next to `monkify-meta`
   (see `AGENTS.md` → "ensure the sibling repos are cloned").
2. **Clean working trees** — in every repo you will touch, `git status` must show no uncommitted
   changes. If dirty, **stop** and ask the user to commit, stash, or discard first.
3. **Trello MCP** — server `user-trello`. Verify with `list_boards`. If missing, **stop**:
   > "The Trello MCP is not available. Install/enable the `trello` MCP server, then re-run
   > `/monkify-ship-feature`."
   (Branch and spec are read from the card; shipping without Trello is not supported.)

Do not merge until prerequisites pass.

## Workflow

Copy this checklist and track progress:

```
- [ ] Step 0: Verify prerequisites
- [ ] Step 1: Resolve board, find card, read everything + parse Implementation
- [ ] Step 2: Load context (INDEX, spec, constraints, current KB)
- [ ] Step 3: Determine affected repos and confirm with user
- [ ] Step 4: Merge branch → main (each affected repo, in an isolated worktree; register it on Trello)
- [ ] Step 5: Verify merged code (backend tests)
- [ ] Step 6: Extract feature knowledge (spec + Trello + code on main)
- [ ] Step 7: Update the knowledge base
- [ ] Step 8: Review with user; commit meta when asked
- [ ] Step 9: Trello — move card to Implemented
- [ ] Step 10: Report
```

### Step 1: Resolve board, find card, read everything + parse Implementation

Board: `https://trello.com/b/Yi42y6Xy/monkaify` (short id `Yi42y6Xy`).

1. `list_boards` → find `monkaify`; `set_active_board` with its `boardId`.
2. **Find the card** by name across lists (`get_cards_by_list_id`). Match **exact name first**; if
   ambiguous, show matches and ask the user to pick one. Capture `cardId` and `cardUrl`.
3. Read **everything** on the card:
   - `get_card` (`includeMarkdown: true`) — full description
   - `get_card_comments`
   - `get_acceptance_criteria`
4. **Parse `## Implementation`** from the description (see
   `../monkify-create-spec/references/spec-card-metadata.md`):
   - **`branch`** — from `**Branch:**` (value inside backticks, e.g. `feature/free-bet-mode`)
   - **`specPath`** — from `**Spec:**` (e.g. `monkify-meta/specs/free-bet-mode.md`); use path
     relative to `monkify-meta/` for reads: `specs/free-bet-mode.md`
   - **`repos`** — from `**Repos:**` (comma-separated list; default all three if line missing)
   - **`worktree`** — from `**Worktree:**` (path inside backticks; optional — the registered location
     of the feature work. Absent on older cards; that is fine.)
5. Verify `monkify-meta/<specPath-relative>` exists on disk. If missing, **stop**.

Keep `cardId`, `branch`, `specPath`, `repos`, and `worktree` for the rest of the workflow.

### Step 2: Load context

Read, in order:

1. `monkify-meta/INDEX.md` → follow links to `constraints.md`, `architecture.md`, `glossary.md`,
   `conventions/`, and all of `features/`.
2. The **spec file** from the card (`specs/<name>.md`) — note affected repos, summary, constraints,
   data model, API surface, and business rules.
3. Any existing `features/<name>.md` that already distills this spec (may be a stub).

The goal: know what the KB claims **today** vs what the spec says the feature **should** do after
merge.

### Step 3: Determine affected repos and confirm

From the card **`repos`** line and the spec, list every repo that will be merged (intersect with
what the feature actually needs — prefer the card's **Repos** list when present):

- `monkify-backend` — almost always for behavioral features.
- `monkify-front` — when the UI or client API usage changes.
- `monkify-meta` — when the feature branch has spec/KB commits not yet on `main`.

Present the user a short plan **before any merge**:

> Branch `<branch>` → `main` in: [repos]. Spec: `<specPath>`. Card: [title + link]. KB files to update: [list].

**Wait for explicit confirmation** before Step 4. Merging is not reversible from this skill.

### Step 4: Merge branch → main (each affected repo, in an isolated worktree)

Per `AGENTS.md` must-follow rule 6, do the merge in an **isolated git worktree** checked out to
`main` — never mutate the shared checkout. For **each** repo in the plan:

```bash
# from <workspace-root>/<repo>
git fetch origin
git worktree add ../.worktrees/ship-<slug>/<repo> main   # isolated main checkout for the merge
cd ../.worktrees/ship-<slug>/<repo>
git pull origin main
git merge origin/<branch>   # (or `git merge <branch>` if the branch is local-only). Use the card's branch.
```

**Register the merge worktree on the card** (see `references` → spec-card-metadata "Worktree
registration"): once, before merging, `add_comment`:

```markdown
🌳 Worktree registered (YYYY-MM-DD) — shipping <branch> → main from `.worktrees/ship-<slug>/<repo>` (per repo)
```

**On merge conflict:** stop immediately. Report the repo, conflicting files, and do **not** continue
to other repos or KB updates until the user resolves conflicts (or asks you to abort).

**Hard rules:**

- Never `git push --force`, never reset `--hard`, never skip hooks.
- Never update git config.
- Keep worktrees in an ignored/untracked location (the workspace-root `.worktrees/` dir).
- After each successful merge, **`git push origin main`** in that worktree (see `AGENTS.md` → push
  after branch work).

After all merges succeed locally, summarize: repo, commits merged, any notable files touched.

### Step 5: Verify merged code

When `monkify-backend` was merged:

1. Run `Monkify.Tests` from `monkify-backend` (full suite).
2. Run the **`Monkify.Tests.Integration`** Testcontainers suite via
   `scripts/run_integration_tests.bat` (Docker must be running). Per `constraints.md` → "Integration
   tests are mandatory for every new feature", a **feature** must ship with at least one happy-path
   integration test (plus one per complex flow). If the merged feature is a genuine feature and has
   **no** integration test covering its happy path, **stop** and flag the gap — the feature is not
   ready to ship. (Bug fixes / QOL / minor changes are exempt from the integration-test requirement.)
3. If any suite fails, **stop** — report failures; do not declare the feature shipped or finalize KB
   updates that claim behavior the tests contradict.
4. Note which test areas (unit + integration) cover the new feature (for the report).

When only frontend/meta merged, smoke-check that the spec's API routes and types still match what
you read on `main` (spot-check controllers/handlers and front services/types).

### Step 6: Extract feature knowledge

Build a **feature digest** the KB update will draw from. The Trello card was already loaded in
Step 1 — use it for product intent, edge cases, acceptance criteria, and scope boundaries. Do
**not** treat unchecked acceptance criteria as shipped behavior unless **code on `main`** confirms
them.

#### Sources for the digest

| Source | What to extract |
| --- | --- |
| **Trello card** (if loaded) | Problem, refinement decisions, backend/frontend impact notes, edge cases, out of scope, acceptance criteria |
| Spec file | Product rules, edge cases, API contract, constraints |
| Merge diff / `git log` | What actually landed |
| Live code on `main` | Entities, endpoints, headers, consumers, front routes/composables |
| Existing `features/*.md` | What was already distilled — reconcile, don't duplicate stale text |

Walk `references/knowledge-update-checklist.md` and fill in every section that applies. Flag drift
between **code**, **spec**, and **Trello**; prefer **code on `main`** for "what runs today" and
note drift in the report. When Trello and spec disagree, prefer spec for design intent unless code
clearly followed the Trello refinement.

### Step 7: Update the knowledge base

Edit `monkify-meta` on `main` (after merging the meta branch if needed). Treat shipped behavior as
**current truth** — see INDEX.md → "Keeping the knowledge base current".

Minimum updates (as relevant — do not touch unrelated files):

| Document | When / what |
| --- | --- |
| `features/<feature>.md` | **Always.** Distilled rules + code touchpoints. Use `references/feature-doc-template.md`. |
| `architecture.md` | New/changed services, flows, integrations, or endpoints. |
| `glossary.md` | New or changed domain terms; fix definitions that the feature superseded. |
| `conventions/backend.md` or `frontend.md` | New patterns the feature introduced. |
| `constraints.md` | Only if the feature adds or clarifies **non-negotiable** rules already enforced in code. |
| `INDEX.md` | Feature map row status; remove or update "Feature currently in flight"; add skill cross-links if needed. |
| The spec file | Add/update a **Status: Shipped** (date, branch) banner at the top — spec stays the deep design archive. |

**Business rules:** the distilled rules in `features/<feature>.md` plus any glossary/architecture
sections are how agents learn "how Monkify works." Write them as **present-tense behavior** (what
the system does now), not as a todo list. Include edge cases from the spec **and Trello card** that
survived in code.

Do **not** delete the spec — `features/` summarizes; `specs/` stays authoritative history.

### Step 8: Commit the knowledge-base sync (no review needed)

Show the user a concise summary of KB diffs (files changed + bullet list of rule changes) for
their awareness. Then, because syncing the KB is part of this skill's workflow, **commit and push
the `monkify-meta` changes without waiting for review** (see `AGENTS.md` → Must-follow rule 7 and
`constraints.md` → §2):

- Commit in `monkify-meta` with a message focused on *why* (e.g. "Sync KB after shipping operators
  feature"), then **`git push origin main`** immediately.
- This auto-commit exception is scoped to the **KB sync at the end of `monkify-ship-feature`**. It
  does **not** extend to committing code repos (`monkify-backend` / `monkify-front`) to `main` —
  those merges happen in Step 4 as part of the shipping workflow, but any *other* `main` commit
  still needs explicit user approval.

### Step 9: Trello — move card to Implemented

Using the `cardId` from Step 1:

1. `get_lists` → capture the **`Implemented`** list id. If no list is named exactly `Implemented`,
   ask the user which list to use.
2. `move_card` with the `cardId`, the **`Implemented`** list id, and `boardId`.
3. Optionally `add_comment` with a one-line ship note (branch merged, spec file, date).
4. **Retire the worktree(s)** — the feature branch is now merged, so its worktree can be removed.
   `add_comment` (see `references` → spec-card-metadata "Worktree registration"):

   ```markdown
   🌳 Worktree retired (YYYY-MM-DD) — `feature/<slug>` merged to main; feature + ship worktrees can be removed.
   ```

Do **not** rename the card — only move it to **Implemented**. Preserve the card title and description.

### Step 10: Report

Tell the user:

1. **Card** — name + link; branch, spec, and **worktree** read from **Implementation** section.
2. **Merged:** `<branch>` → `main` per repo (from an isolated worktree, **pushed** to `origin`);
   worktree **registered** on the card before merge and **retired** after moving to Implemented.
3. **Verified:** test run result (backend).
4. **KB updated:** list of files and the headline business-rule changes.
5. **Drift:** any code vs spec vs Trello mismatches worth a follow-up.
6. **Trello:** moved to **Implemented** (or skipped if move failed).
7. **Next:** deploy or manual QA — only if relevant.

## Hard rules

- **Confirm before merge** — never merge without explicit user approval of the plan in Step 3.
- **Stop on conflict or test failure** — partial KB updates that contradict failing tests are worse
  than stopping.
- Honor `constraints.md` — vague operator messaging, mock-only secrets, branch-per-repo history.
- **Code on `main` wins** for runtime behavior; the spec wins for *design intent*; the **Trello card**
  captures refinement decisions and acceptance criteria — use all three when ingesting, reconcile in
  the digest.
- This skill does **not** implement features or write new specs — use `monkify-refine-tasks` and
  `monkify-create-spec` earlier in the pipeline. **Branch and spec come from the Trello card**, not
  from the user at ship time.

## Trello tools used (server `trello` / `user-trello`)

`list_boards`, `set_active_board`, `get_lists`, `get_cards_by_list_id`, `get_card`,
`get_card_comments`, `get_acceptance_criteria`, `move_card`, `add_comment` (optional).

## References

- `../monkify-create-spec/references/spec-card-metadata.md` — **Implementation** block format on the card.
- `references/knowledge-update-checklist.md` — which KB files to update and what to pull from each.
- `references/feature-doc-template.md` — structure for `features/<name>.md` after shipping.
