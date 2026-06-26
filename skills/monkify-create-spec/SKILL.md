---
name: monkify-create-spec
description: Turn a refined Monkify Trello card into an implementation spec, create feature branches in each affected repo, and move the card to Specs created. Use when the user names a Refined card and wants a spec file under monkify-meta/specs/, branch setup, and Trello pipeline advance — after monkify-refine-tasks, before implementation. Needs Trello MCP and sibling repos.
disable-model-invocation: true
---

# Monkify — Create Spec

Turn a **Refined** Trello card into an authoritative **`monkify-meta/specs/<feature>.md`**
implementation spec, create **`feature/<slug>`** branches in every affected repo, and move the card
to **Specs created**.

This skill **writes the spec and creates branches only** — it does **not** implement code.

Board: `https://trello.com/b/Yi42y6Xy/monkaify` (short id `Yi42y6Xy`).

## Pipeline position

```
Backlog → Refined  →  Specs created  →  (implement)  →  Implemented
           ↑ refine              ↑ this skill
         monkify-refine-tasks    monkify-create-spec
```

## Input (required)

The user names a **Trello card** (exact title or unambiguous substring), e.g.:

> `/monkify-create-spec Add Free bet mode`

If the name is ambiguous, show matches and ask which card.

## Prerequisites (verify FIRST — stop if missing)

1. **Trello MCP** — server `user-trello`. Verify with `list_boards`. If missing, **stop**:
   > "The Trello MCP is not available. Install/enable the `trello` MCP server, then re-run
   > `/monkify-create-spec`."
2. **Sibling repos** — `monkify-backend`, `monkify-front`, and `monkify-meta` exist next to each
   other (see `AGENTS.md`).
3. **Clean git trees** — in every repo where you will create a branch, `git status` must show no
   uncommitted changes. If dirty, **stop** and ask the user to commit, stash, or discard first.

## Workflow

Copy this checklist and track progress:

```
- [ ] Step 0: Verify prerequisites
- [ ] Step 1: Load Monkify context
- [ ] Step 2: Resolve board, find card, read everything
- [ ] Step 3: Merge conversation context (if applicable)
- [ ] Step 4: Validate card is spec-ready
- [ ] Step 5: Explore codebase for accurate touchpoints
- [ ] Step 6: Derive slug, branch name, affected repos, spec path
- [ ] Step 7: Confirm plan with user (if card is thin or repos unclear)
- [ ] Step 8: Create feature branches (each affected repo)
- [ ] Step 9: Write spec file
- [ ] Step 10: Update INDEX.md (feature in flight)
- [ ] Step 11: Update card with spec path + branch name
- [ ] Step 12: Move card to Specs created
- [ ] Step 13: Report
```

### Step 1: Load Monkify context

Read, in order:

1. `monkify-meta/INDEX.md`
2. `monkify-meta/constraints.md`
3. Relevant `monkify-meta/features/*.md` (all files — know what exists)
4. `monkify-meta/architecture.md` when the card touches flows, messaging, or integrations
5. **`monkify-meta/specs/operator-feature.md`** — follow its **format** (section numbering, tone,
   file paths, mermaid usage). Shipped specs are style references; do not copy unrelated content.

### Step 2: Resolve board, find card, read everything

1. `list_boards` → find `monkaify` (`Yi42y6Xy`); `set_active_board`.
2. `get_lists` → capture list ids, especially **`Refined`** and **`Specs created`**.
3. **Find the card** by name across lists (`get_cards_by_list_id` on each open list, or search from
   Backlog + Refined + Specs created). Exact name match first; if multiple matches, ask the user.
4. Read **everything** on the card:
   - `get_card` (`includeMarkdown: true`) — full description
   - `get_card_comments`
   - `get_acceptance_criteria`

Keep `cardId`, `cardUrl`, and current list name.

### Step 3: Merge conversation context (if applicable)

When this skill runs **in the same chat** where refinement or design discussion already happened:

- Treat the user's confirmed answers and any **approved design summary** in the thread as
  **authoritative** alongside the Trello card.
- Prefer conversation decisions over stale card text when they disagree (the card may not have been
  updated yet).
- Do **not** re-ask refinement questions already answered in the conversation.

When invoked in a **fresh chat** with only a card name, rely on the card (+ comments + acceptance
criteria) only.

### Step 4: Validate card is spec-ready

**Stop** and tell the user to run `/monkify-refine-tasks` first if:

- The card is still in **Backlog** (or any list other than **Refined** or **Specs created**).
- The description lacks **Problem**, **Decisions**, and **Affected repos** sections (refined template
  in `monkify-refine-tasks/references/card-template.md`).
- There are **no** acceptance-criteria checklist items **and** the description has open ambiguities
  a spec-writer would have to guess about.

If the card is already in **Specs created** and a spec file exists, ask whether to **regenerate**
the spec or skip to reporting.

### Step 5: Explore codebase for accurate touchpoints

The spec must name **real files and behaviours** — not generic placeholders.

Walk `references/code-exploration-checklist.md` for the feature areas the card touches. Use search
and read to confirm:

- Handlers, consumers, domain services, EF mapping, DTOs, API controllers
- Frontend composables, services, types, components, env var patterns (`VUE_APP_*`)
- Existing validation that would **block** the new behaviour today
- Test files to extend (`Monkify.Tests`)

**Flag gaps** discovered in code (e.g. player-count logic, required DTO fields) and document the
**required fix** in the spec — do not leave them for the implementer to discover.

### Step 6: Derive slug, branch name, affected repos, spec path

| Item | Rule |
| --- | --- |
| **Slug** | Lowercase kebab-case from card title: `Add Free bet mode` → `free-bet-mode` |
| **Branch** | `feature/<slug>` in **each** affected repo |
| **Spec file** | `monkify-meta/specs/<slug>.md` |
| **Affected repos** | From card **Affected repos** checkboxes; default both apps if full-stack |

**Always include `monkify-meta`** in branch creation (the spec lives there).

Present a one-line plan before branching when anything is non-obvious:

> Card: *…* · Spec: `specs/free-bet-mode.md` · Branches: `feature/free-bet-mode` in backend, front, meta.

Skip confirmation when the card is fully refined and repos are explicit — proceed.

### Step 7: Confirm plan with user (conditional)

Ask before Step 8 only when:

- Affected repos are ambiguous (card says backend-only but mentions UI copy).
- Slug collision — `specs/<slug>.md` already exists for a **different** feature.
- Card is in **Refined** but missing acceptance criteria **and** conversation context is absent.

Otherwise proceed without blocking.

### Step 8: Create feature branches (each affected repo)

For **each** repo (`monkify-backend`, `monkify-front`, `monkify-meta` as applicable), from
`<workspace-root>/<repo>`:

```bash
git fetch origin
git checkout main
git pull origin main
git checkout -b feature/<slug>
git push -u origin feature/<slug>
```

**Hard rules:**

- Never commit directly on `main`.
- Never update git config; never force-push; never skip hooks.
- **Push** each new branch to `origin` right after creation (see `AGENTS.md` → push after branch work).
- After any later commit on the feature branch (when the user asks you to commit), **push again**
  before moving on.

If `main` is missing or branch already exists, stop and report — ask how to proceed.

### Step 9: Write spec file

Create `monkify-meta/specs/<slug>.md` on the **`feature/<slug>`** branch using
`references/spec-template.md`.

**Requirements:**

- Header: **Status: Ready for implementation**, Trello card link, repos affected, one-paragraph purpose.
- Include **§2 Constraints** pointing at `constraints.md` (branch-per-repo, unit tests, EF inline,
  migrations via Consumers, mock secrets, vague operator messaging).
- **§3 Resolved product decisions** — table from card **Decisions** + conversation context.
- **§4+** — data model, API, backend/frontend behaviour, diagrams where flows are non-obvious.
- **File checklist** — every file to edit with one-line change summary.
- **Unit tests** — concrete test cases mapped to acceptance criteria.
- **§ Acceptance criteria** — copy from Trello checklist (or synthesize from conversation if card
  checklist is empty but refinement was done in chat).
- **§ Manual QA** — short dev verification steps when useful.
- **§ Trello / pipeline** — link card; note next step is implementation then `/monkify-ship-feature`.
- **Revision log** — date + source.

**Self-review before saving** (fix inline):

1. No TBD / placeholder sections.
2. No contradictions between summary, decisions, and behaviour sections.
3. Frontend env vars use **`VUE_APP_*`** (not `VITE_*`) unless the codebase has migrated.
4. Spec scope matches card **Out of scope**.
5. Code exploration findings (hidden blockers) are documented.

**Do not** implement code in this step.

### Step 10: Update INDEX.md

On the same `monkify-meta` branch, edit `INDEX.md`:

1. Add a row to the knowledge-base map for `specs/<slug>.md` (status **Ready**).
2. Update **Feature currently in flight** with: feature name, spec path, Trello link, branch name
   in each affected repo.

Do **not** commit unless the user asks (workspace rule). The spec file and INDEX edit should be
ready to commit together when requested.

### Step 11: Update card with spec path + branch name

Before moving the card, persist implementation metadata **in the card description** so
`/monkify-ship-feature` can read branch and spec without the user re-entering them.

1. `update_card_details` — merge into the existing description (preserve Problem, Decisions,
   acceptance-criteria-related sections, etc.):
   - Add or replace an **`## Implementation`** block per `references/spec-card-metadata.md`:
     - **Spec:** `monkify-meta/specs/<slug>.md`
     - **Branch:** `feature/<slug>`
     - **Repos:** comma-separated affected repos (always include `monkify-meta`)
   - Set **`## Status`** to `Spec created — ready for implementation.` (replace
     `Refined — ready for spec.` if present).

Do **not** move the card if Step 9 failed or the spec file was not written.

### Step 12: Move card to Specs created

1. `get_lists` → **`Specs created`** list id.
2. `move_card` with `cardId`, target list id, and `boardId`.
3. Optionally `add_comment` (short pointer only — branch/spec live in the description):

   ```markdown
   **Spec created (YYYY-MM-DD)** — see **Implementation** section in description.
   ```

### Step 13: Report

Tell the user:

1. **Card** — name + link; moved to **Specs created**; **Implementation** section written.
2. **Spec** — full path `monkify-meta/specs/<slug>.md` (brief summary of what it covers).
3. **Branch** — `feature/<slug>` created and **pushed** in which repos.
4. **INDEX** — updated "Feature currently in flight".
5. **Code findings** — any non-obvious blockers documented in the spec.
6. **Next** — implement on the feature branches; run `/monkify-ship-feature <card name>` when merged.

## Trello tools used (server `trello` / `user-trello`)

`list_boards`, `set_active_board`, `get_lists`, `get_cards_by_list_id`, `get_card`,
`get_card_comments`, `get_acceptance_criteria`, `update_card_details`, `move_card`, `add_comment`.

## Hard rules

- **No implementation** — spec + branches + Trello move only.
- **No refinement** — if the card is not spec-ready, send the user to `/monkify-refine-tasks`.
- Honor `constraints.md` — mock secrets, vague operator messaging, branch-per-repo.
- Follow **`specs/operator-feature.md`** structure; use **`specs/free-bet-mode.md`** as a recent
  example of a card-sourced spec.
- Never commit unless the user explicitly asks. **Always push** after creating or updating a branch
  (see `AGENTS.md`).

## References

- `references/spec-template.md` — spec section outline and quality bar.
- `references/spec-card-metadata.md` — **Implementation** block written to the Trello card (read by ship-feature).
- `references/code-exploration-checklist.md` — what to read in backend/front when drafting the spec.
- `../monkify-refine-tasks/references/card-template.md` — expected refined card shape (input format).
