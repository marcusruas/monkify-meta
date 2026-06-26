---
name: monkify-ship-feature
description: Merge a Monkify feature branch into main and sync the monkify-meta knowledge base from the spec, linked Trello task, and merged code. Use when the user wants to ship, land, or merge a feature and update architecture, features, glossary, conventions, and business rules so agents reflect current behavior. Takes a branch name and a spec file under monkify-meta/specs/. Optionally ingests the monkaify Trello card and moves it to Implemented.
disable-model-invocation: true
---

# Monkify — Ship Feature

Merge a completed feature into `main` in every affected repo, then update the **monkify-meta**
knowledge base so it describes how Monkify actually works after the merge — not how it worked
before.

This skill **merges code and rewrites docs**; it does not implement features.

## Inputs (required — ask if missing)

| Input | Example | Notes |
| --- | --- | --- |
| **Branch** | `feature/operators` | Same branch name in each affected repo (per `constraints.md`). |
| **Spec file** | `specs/operator-feature.md` | Path relative to `monkify-meta/`. Authoritative design source. |
| **Trello card** | *(optional)* `Enhance operator feature` | Card name on the **monkaify** board. Used when ingesting feature knowledge and when moving to **Implemented**. Ask in Step 5 if not provided upfront. |

Resolve both before Step 1. If the user only gave a feature name, infer the spec from
`monkify-meta/specs/` and confirm.

## Prerequisites (verify FIRST — stop if missing)

1. **Sibling repos** — `monkify-backend` and `monkify-front` exist next to `monkify-meta`
   (see `AGENTS.md` → "ensure the sibling repos are cloned").
2. **Clean working trees** — in every repo you will touch, `git status` must show no uncommitted
   changes. If dirty, **stop** and ask the user to commit, stash, or discard first.
3. **Spec file exists** — read `monkify-meta/<spec-file>`. If missing, **stop**.

Do not merge until prerequisites pass.

## Workflow

Copy this checklist and track progress:

```
- [ ] Step 0: Verify prerequisites
- [ ] Step 1: Load context (INDEX, spec, constraints, current KB)
- [ ] Step 2: Determine affected repos and confirm with user
- [ ] Step 3: Merge branch → main (each affected repo)
- [ ] Step 4: Verify merged code (backend tests)
- [ ] Step 5: Extract feature knowledge (spec + Trello + code on main)
- [ ] Step 6: Update the knowledge base
- [ ] Step 7: Review with user; commit meta when asked
- [ ] Step 8: Trello — move card to Implemented (optional)
- [ ] Step 9: Report
```

### Step 1: Load context

Read, in order:

1. `monkify-meta/INDEX.md` → follow links to `constraints.md`, `architecture.md`, `glossary.md`,
   `conventions/`, and all of `features/`.
2. The **spec file** the user named — note affected repos, summary, constraints, data model, API
   surface, and business rules.
3. Any existing `features/<name>.md` that already distills this spec (may be a stub).

The goal: know what the KB claims **today** vs what the spec says the feature **should** do after
merge.

### Step 2: Determine affected repos and confirm

From the spec (and branch name), list every repo that will be merged:

- `monkify-backend` — almost always for behavioral features.
- `monkify-front` — when the UI or client API usage changes.
- `monkify-meta` — when the feature branch has spec/KB commits not yet on `main`.

Present the user a short plan **before any merge**:

> Branch `feature/…` → `main` in: [repos]. Spec: `specs/….md`. KB files to update: [list].

**Wait for explicit confirmation** before Step 3. Merging is not reversible from this skill.

### Step 3: Merge branch → main (each affected repo)

For **each** repo in the plan, run from `<workspace-root>/<repo>`:

1. `git fetch origin`
2. `git checkout main`
3. `git pull origin main`
4. `git merge origin/<branch>` (or `git merge <branch>` if the branch is local-only — prefer the
   remote tracking branch when it exists)

**On merge conflict:** stop immediately. Report the repo, conflicting files, and do **not** continue
to other repos or KB updates until the user resolves conflicts (or asks you to abort).

**Hard rules:**

- Never `git push --force`, never reset `--hard`, never skip hooks.
- Never update git config.
- Do not push unless the user explicitly asks.

After all merges succeed locally, summarize: repo, commits merged, any notable files touched.

### Step 4: Verify merged code

When `monkify-backend` was merged:

1. Run `Monkify.Tests` from `monkify-backend` (full suite).
2. If tests fail, **stop** — report failures; do not declare the feature shipped or finalize KB
   updates that claim behavior the tests contradict.
3. Note which test areas cover the new feature (for the report).

When only frontend/meta merged, smoke-check that the spec's API routes and types still match what
you read on `main` (spot-check controllers/handlers and front services/types).

### Step 5: Extract feature knowledge

Build a **feature digest** the KB update will draw from.

#### 5a. Trello card (optional but recommended)

At the start of this step, if the user has not already named a Trello card, ask:

> Do you have a Trello card for this feature? If so, what is the **card name**?

- If **no card**, continue with spec + code only; Step 8 will ask again only if the user wants to
  move a card later.
- If the user **names a card**, load the full task from the **monkaify** board
  (`https://trello.com/b/Yi42y6Xy/monkaify`, short id `Yi42y6Xy`):

1. **Trello MCP** — server `user-trello` (serverName `trello`). Verify with `list_boards`. If
   missing, **stop** and tell the user:
   > "The Trello MCP is not available. Install/enable the `trello` MCP server, then re-run this step
   > with the card name — or continue without Trello if you prefer."
   Allow the user to proceed without Trello (spec + code only) if they choose.
2. `list_boards` → find `monkaify`; `set_active_board` with its `boardId`.
3. **Find the card** by name: `get_cards_by_list_id` across the board's lists. Match **exact name
   first**; if ambiguous, show matches and ask the user to pick one. **Keep `cardId`** for Step 8.
4. Read **everything** on the card (same as `monkify-refine-tasks` Step 3):
   - `get_card` (`includeMarkdown: true`) — name + description (Problem, Decisions, Backend/
     Frontend impact, Edge cases, Out of scope, etc.).
   - `get_card_comments` — prior discussion and decisions.
   - `get_acceptance_criteria` — Given/When/Then checklist items.

Use the Trello task for **product intent, edge cases, acceptance criteria, and scope boundaries**
that may not be spelled out in the spec or obvious from the diff. Do **not** treat unchecked
acceptance criteria as shipped behavior unless **code on `main`** confirms them.

#### 5b. Sources for the digest

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

### Step 6: Update the knowledge base

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

### Step 7: Review with user; commit meta when asked

Show the user a concise summary of KB diffs (files changed + bullet list of rule changes).

- **Do not commit** meta changes until the user asks (workspace rule).
- When they ask, commit in `monkify-meta` with a message focused on *why* (e.g. "Sync KB after
  shipping operators feature").
- Push only when explicitly requested.

If code repos were merged locally but not pushed, remind the user to push `main` in each repo when
ready — do not push without being asked.

### Step 8: Trello — move card to Implemented (optional)

If a card was loaded in Step 5 (`cardId` already captured), **move it now** — do not ask for the name
again unless the user asks to use a different card.

If **no card** was loaded in Step 5, ask once:

> Should I move a Trello card to **Implemented**? If so, what is the **card name**?

Then on the **monkaify** board:

1. If Trello MCP was not verified in Step 5, verify now (`list_boards`). If missing, skip and tell
   the user how to enable it.
2. If `cardId` is not yet known: `set_active_board`, find the card by name (same search rules as
   Step 5a), capture `cardId`.
3. `get_lists` → capture the **`Implemented`** list id. If no list is named exactly `Implemented`,
   ask the user which list to use.
4. `move_card` with the `cardId`, the **`Implemented`** list id, and `boardId`.
5. Optionally `add_comment` with a one-line ship note (branch merged, spec file, date) — skip if a
   similar comment was already added.

Do **not** rename the card — only move it to **Implemented**. Preserve the card title and description.

### Step 9: Report

Tell the user:

1. **Merged:** branch → `main` per repo (and whether push is still pending).
2. **Verified:** test run result (backend).
3. **KB updated:** list of files and the headline business-rule changes.
4. **Ingested from:** spec; Trello card (if any); code on `main`.
5. **Drift:** any code vs spec vs Trello mismatches worth a follow-up.
6. **Trello:** card name + link if moved to **Implemented**; skipped if none.
7. **Next:** push remotes or deploy — only if relevant.

## Hard rules

- **Confirm before merge** — never merge without explicit user approval of the plan in Step 2.
- **Stop on conflict or test failure** — partial KB updates that contradict failing tests are worse
  than stopping.
- Honor `constraints.md` — vague operator messaging, mock-only secrets, branch-per-repo history.
- **Code on `main` wins** for runtime behavior; the spec wins for *design intent*; the **Trello card**
  captures refinement decisions and acceptance criteria — use all three when ingesting, reconcile in
  the digest.
- This skill does **not** implement features or write new specs — use `monkify-refine-tasks` earlier
  in the pipeline. It only **moves** an existing card to **Implemented** when the user names one.

## Trello tools used (server `trello` / `user-trello`)

`list_boards`, `set_active_board`, `get_lists`, `get_cards_by_list_id`, `get_card`,
`get_card_comments`, `get_acceptance_criteria`, `move_card`, `add_comment` (optional).

## References

- `references/knowledge-update-checklist.md` — which KB files to update and what to pull from each.
- `references/feature-doc-template.md` — structure for `features/<name>.md` after shipping.
