---
name: monkify-ship-feature
description: Ship a completed Monkify feature — sync the monkify-meta knowledge base on the feature branch, then merge the branch into main in every affected repo and clean up. Use when the user wants to ship, land, or merge a feature. Takes a Trello card name on the monkaify board; reads branch, spec, worktree, repos and an optional release branch from the card's Implementation section. Supports RELEASE TRAINS: when the card names a release branch, the feature merges into that branch instead of main and the card stays in Implemented; invoking the skill with a release/<name> branch then merges the whole train into main and ships every card on it. Updates the KB on the feature branch BEFORE merging (so it lands with the feature), moves the card to Shipped, and deletes the merged branches and worktrees when done.
disable-model-invocation: true
---

# Monkify — Ship Feature

Ship a completed feature: first **sync the monkify-meta knowledge base on the feature branch** so the
docs describe how Monkify works **after** this feature, then **merge the feature branch into `main`**
in every affected repo (KB included), move the card to **Shipped**, and **delete the merged branches
and their worktrees**.

Order matters: **the KB is updated and committed on `feature/<slug>` BEFORE the merge**, so the docs
land on `main` in the same merge as the feature — not as a separate follow-up commit.

This skill **syncs docs and merges code**; it does not implement features (that's
`monkify-implement-feature`) or write specs (that's `monkify-create-spec`).

## Release trains (when `main` must not see the feature yet)

Some features are split across several cards and are only coherent once **all** of them land — the
Orbit epic is the case this exists for. Merging each child straight to `main` would leave `main`
carrying a half-built capability: a visible surface whose engine ships two cards later.

So a card may nominate a **release branch** in its `## Implementation` block:

```markdown
- **Release branch:** `release/orbit`
```

That one line changes three things, and nothing else:

| | no release branch (default) | release branch named |
| --- | --- | --- |
| Step 8 merges `feature/<slug>` into | `main` | **`release/<name>`** |
| Step 9 moves the card to | **Shipped** | nowhere — it **stays in Implemented** |
| Step 10 deletes | the feature branch | the feature branch (the **release branch survives**) |

Everything else — the verification in Step 5, the KB sync in Step 7, the worktree discipline — is
unchanged. **The KB is still synced per card**, onto its own feature branch, so each card's docs
travel with its code and reach `main` together with the rest of the train.

The train is then landed by **form B**:

> `/monkify-ship-feature release/orbit`

which merges `release/<name>` → `main` in every repo that has that branch, moves **every** card
naming it to **Shipped**, and deletes the release branch. See **Step R** at the end of this file.

⚠️ **Children stay in `Implemented` on purpose.** A card whose code is on a release branch is genuinely
not shipped — `main` has never seen it — and the board must not claim otherwise. The trade-off,
accepted deliberately: a finished card can sit in Implemented for a while, so the ship comment written
in Step 9 is what tells a reader it is done and waiting on the train.

## Pipeline position

```
Backlog → Refined → Specs created → Implemented → Shipped
                     ↑ create-spec   ↑ implement    ↑ THIS SKILL
```

## Input (required)

Two forms. Which one the user typed decides everything downstream, so resolve it first.

**A — ship a card.** The user names the **Trello card** (exact title or unambiguous substring):

> `/monkify-ship-feature Add Free bet mode`

**B — ship a release train.** The user names a **`release/<name>` branch** instead of a card:

> `/monkify-ship-feature release/orbit`

Form B is recognised by the argument starting with `release/`. It merges that branch into `main` in
every repo that has it and ships **every** card riding the train — see "Release trains" below.

**Branch**, **spec file**, **worktree**, and **repos** are **not** asked separately — read them from
the card's **`## Implementation`** section (written by `/monkify-create-spec`). See
`../monkify-create-spec/references/spec-card-metadata.md`.

If the card has no **Implementation** block or **Branch** line, **stop** and tell the user to run
`/monkify-create-spec` (or add the block manually).

## Prerequisites (verify FIRST — stop if missing)

1. **Sibling repos** — `monkify-backend`, `monkify-front`, and `monkify-meta` exist next to each
   other (see `AGENTS.md`).
2. **Clean `main` hubs** — in every repo you will touch, the shared checkout must be **clean and on
   `main`** (it's the permanent hub; this skill updates the KB in the feature worktree and merges in a
   short-lived ship worktree, never in the hub). If a hub is dirty or on a feature branch, **stop**
   and ask the user to restore it to a clean `main`.
3. **Trello MCP** — server `user-trello`. Verify with `list_boards`. If missing, **stop**:
   > "The Trello MCP is not available. Install/enable the `trello` MCP server, then re-run
   > `/monkify-ship-feature`."
   (Branch and spec are read from the card; shipping without Trello is not supported.)

Do not update the KB or merge until prerequisites pass.

## Workflow

Copy this checklist and track progress:

```
- [ ] Step 0: Verify prerequisites
- [ ] Step 1: Resolve board, find card, read everything + parse Implementation
- [ ] Step 2: Load context (INDEX, spec, constraints, current KB)
- [ ] Step 3: Determine affected repos and confirm the ship plan with the user
- [ ] Step 4: Ensure the feature worktree(s) exist (recreate from origin if missing)
- [ ] Step 5: Verify the feature on its branch (backend unit + integration tests)
- [ ] Step 6: Extract feature knowledge (spec + Trello + code on the branch)
- [ ] Step 7: Update the KB on the monkify-meta feature branch; show diff; commit + push feature/<slug>
- [ ] Step 8: Merge feature/<slug> → the TARGET (main, or release/<name>) in each affected repo; push
- [ ] Step 9: Trello — move card to Shipped, or leave it in Implemented on a release train
- [ ] Step 10: Clean up — remove worktrees, delete the merged FEATURE branch (never the release branch), clear the deployed-stack marker
- [ ] Step 11: Report
```

**Form B (`/monkify-ship-feature release/<name>`) runs a different, much shorter checklist:**

```
- [ ] Step 0: Verify prerequisites (Trello MCP, clean main hubs)
- [ ] Step R1: Find every card naming this release branch
- [ ] Step R2: Confirm the release plan with the user (repos, cards, commit counts)
- [ ] Step R3: Verify the release branch (backend unit + integration tests)
- [ ] Step R4: Merge release/<name> → main in each repo that has it; push
- [ ] Step R5: Trello — move EVERY card on the train to Shipped
- [ ] Step R6: Clean up — delete the release branch (local + origin) and any ship worktrees
- [ ] Step R7: Report
```

### Step 1: Resolve board, find card, read everything + parse Implementation

Board: `https://trello.com/b/Yi42y6Xy/monkaify` (short id `Yi42y6Xy`).

1. `list_boards` → find `monkaify`; `set_active_board` with its `boardId`.
2. **Find the card** by name across lists (`get_cards_by_list_id`). Exact name first; if ambiguous,
   show matches and ask the user. Capture `cardId` and `cardUrl`.
3. Read **everything** on the card:
   - `get_card` (`includeMarkdown: true`) — full description
   - `get_card_comments`
   - `get_acceptance_criteria`
4. **Parse `## Implementation`** (see `../monkify-create-spec/references/spec-card-metadata.md`):
   - **`branch`** — from `**Branch:**` (e.g. `feature/free-bet-mode`)
   - **`specPath`** — from `**Spec:**` (e.g. `monkify-meta/specs/free-bet-mode.md`; read it at the
     `monkify-meta`-relative path `specs/free-bet-mode.md`)
   - **`repos`** — from `**Repos:**` (comma-separated; default all three if the line is missing)
   - **`worktree`** — from `**Worktree:**` (base path, e.g. `.worktrees/<slug>`; each repo at
     `<base>/<repo>`)
   - **`releaseBranch`** — from `**Release branch:**` if present (e.g. `release/orbit`). **Absent is
     the normal case** and means "merge to `main`, ship the card". Present means this card rides a
     release train — see "Release trains" above. — where the feature work lives
   - **`level`** — from `**Level:**` (`Feature` or `Minor change`; case-insensitive — anything not
     clearly `Minor change` is treated as `Feature`). If absent, fall back to the card's `## Level`
     section, else default to **`Feature`**. This drives the integration-test check (Step 5) and the
     `features/<slug>.md` doc decision (Step 7).
5. Verify `monkify-meta/<specPath-relative>` exists on disk. If missing, **stop**.

Keep `cardId`, `branch`, `specPath`, `repos`, `worktree`, and `level` for the rest of the workflow.

### Step 2: Load context

Read, in order:

1. `monkify-meta/INDEX.md` → follow links to `constraints.md`, `architecture.md`, `glossary.md`,
   `conventions/`, and all of `features/`.
2. The **spec file** from the card (`specs/<slug>.md`) — affected repos, summary, constraints, data
   model, API surface, business rules.
3. Any existing `features/<slug>.md` that already distills this spec (may be a stub).

Goal: know what the KB claims **today** vs what the feature does now, so Step 7 can rewrite the docs
to current truth.

### Step 3: Determine affected repos and confirm the ship plan

From the card **`repos`** line and the spec, list every repo whose `feature/<slug>` branch will be
merged:

- `monkify-backend` — almost always for behavioral features.
- `monkify-front` — when the UI or client API usage changed.
- `monkify-meta` — **always** (the KB sync in Step 7 commits here, and it merges to `main` like the
  code repos).

Present a short plan **before any KB commit or merge**:

> Ship `<branch>` → `<target>` in: [repos, incl. monkify-meta]. Spec: `<specPath>`. Card: [title + link].

where `<target>` is `main`, or the card's **release branch** when it names one. On a release train, say so
plainly in the plan — **`main` will not receive this feature today, and the card will stay in
Implemented** — so the user is agreeing to that and not to a normal ship.
> KB files to update on the branch first: [list]. Card will move **Backlog→…→Shipped**.

**Wait for explicit confirmation** before Step 5 onward. Merging is not reversible from this skill.
(This confirmation also authorizes the KB sync merge — the KB diff itself is shown in Step 7 before
its commit, but it does not need a *separate* review; see `AGENTS.md` → Must-follow rule 7.)

### Step 4: Ensure the feature worktree(s) exist

The feature and its KB updates are committed on `feature/<slug>` in the worktree(s) at
`.worktrees/<slug>/<repo>`. Per `constraints.md` → "Git", never switch the hub's branch — work in the
worktree.

For **each** affected repo:

```bash
git -C <workspace-root>/<repo> fetch origin
git -C <workspace-root>/<repo> worktree list        # is .worktrees/<slug>/<repo> present?
```

- **Present** → make sure it's current: `git -C <workspace-root>/.worktrees/<slug>/<repo> pull --ff-only origin feature/<slug>`.
- **Missing** (fresh session / cleaned up) → recreate it from the pushed branch (hub must be
  clean-on-`main`):

  ```bash
  git -C <workspace-root>/<repo> worktree add ../.worktrees/<slug>/<repo> feature/<slug>
  # if the local branch is absent, track origin:
  # git -C <workspace-root>/<repo> worktree add ../.worktrees/<slug>/<repo> -b feature/<slug> origin/feature/<slug>
  ```

You especially need the **`monkify-meta`** feature worktree — that's where Step 7 edits the KB.

### Step 5: Verify the feature on its branch

Run this **on the feature branch, in the worktree** (before touching docs or `main`).

When `monkify-backend` is affected:

1. **Unit tests** — from `.worktrees/<slug>/monkify-backend`, run `scripts/run_tests.bat` (excludes
   the slow `ProductionSessions` Monte Carlo suite; never run the full `dotnet test` for this).
2. **Integration tests** — `scripts/run_integration_tests.bat` (Docker must be running). Per
   `constraints.md` → "Integration tests are mandatory for every new feature", a **`Feature`** must
   have at least one happy-path integration test (plus one per complex flow). If the card's `level`
   (Step 1) is `Feature` and it has **no** integration test covering its happy path, **stop** and flag
   the gap — it isn't ready to ship. If `level == Minor change`, the integration test is **exempt**
   (still run whatever suite exists, but its absence is not a blocker).
3. If any suite fails, **stop** — report failures; do not update the KB to claim behavior the tests
   contradict, and do not merge.
4. Note which unit + integration test areas cover the feature (for the report).

When only frontend/meta is affected, smoke-check that the spec's API routes/types still match the
code on the branch (spot-check controllers/handlers and front services/types).

### Step 6: Extract feature knowledge

Build a **feature digest** for the KB update. Use the Trello card (loaded in Step 1) for product
intent, edge cases, acceptance criteria, and scope. Do **not** treat unchecked acceptance criteria as
shipped behavior unless **code on the branch** confirms them.

| Source | What to extract |
| --- | --- |
| **Trello card** | Problem, refinement decisions, backend/frontend impact, edge cases, out of scope, acceptance criteria |
| Spec file | Product rules, edge cases, API contract, constraints |
| Code on `feature/<slug>` | Entities, endpoints, headers, consumers, front routes/composables — what actually landed |
| Existing `features/*.md` | What was already distilled — reconcile, don't duplicate stale text |

Walk `references/knowledge-update-checklist.md` and fill every section that applies. Prefer **code on
the branch** for "what runs"; prefer **spec** for design intent; use the **Trello card** for
refinement decisions. Flag any drift in the report.

### Step 7: Update the KB on the feature branch — show diff, commit, push

Edit `monkify-meta` **in its feature worktree** (`.worktrees/<slug>/monkify-meta`, on `feature/<slug>`)
— **never** in the hub, and **not** on `main`. Treat shipped behavior as **current truth** (INDEX.md →
"Keeping the knowledge base current").

Minimum updates (as relevant — do not touch unrelated files):

| Document | When / what |
| --- | --- |
| `features/<feature>.md` | **When `level == Feature`.** Distilled rules + code touchpoints. Use `references/feature-doc-template.md`. **Exempt when `level == Minor change`** — see the note below. |
| `architecture.md` | New/changed services, flows, integrations, or endpoints. |
| `glossary.md` | New or changed domain terms; fix definitions the feature superseded. |
| `conventions/backend.md` or `frontend.md` | New patterns the feature introduced. |
| `constraints.md` | Only if the feature adds/clarifies a **non-negotiable** rule already enforced in code. |
| `INDEX.md` | Feature map row status; update "Feature currently in flight" → shipped; add skill cross-links if needed. |
| The spec file | Add/update a **Status: Shipped** (date, branch) banner at the top — spec stays the deep design archive. |

> **Feature-doc exemption (driven by `level`):** when the card's `level` (Step 1) is
> **`Minor change`** — a bug fix, QOL / dev-experience tweak, or small change — do **not** create a
> dedicated `features/<feature>.md` (this mirrors the integration-test exemption in Step 5). When
> `level == Feature`, the dedicated doc is **required**. The classification comes from refinement (the
> `## Level` → `## Implementation` `**Level:**` field), not an ad-hoc call here; only override it (with
> the user) if what actually shipped clearly contradicts the recorded level. The exemption only skips
> the *new dedicated feature artifact*, never the "keep the KB current" duty (INDEX.md → "Keeping the
> knowledge base current"):
>
> - **You MUST still update every relevant existing `features/*.md`.** A minor change / bug fix
>   usually changes behavior that an existing feature doc already describes — find those docs and
>   correct them so they match what now runs. Skipping this is not allowed; the dedicated *new* doc
>   is the only thing waived.
> - Also fold the change into `glossary.md`, `architecture.md`, `conventions/`, `constraints.md`,
>   and the spec's **Status: Shipped** banner as relevant.

Write business rules as **present-tense behavior** (what the system does now), not a todo list.
Include edge cases from the spec **and** Trello that survived in code. Do **not** delete the spec —
`features/` summarizes; `specs/` stays authoritative history.

Then **show the user a concise summary of the KB diff** (files changed + bullet list of rule changes)
for awareness, and — because syncing the KB is part of this skill's workflow — **commit and push on
the feature branch without waiting for a separate review** (`AGENTS.md` → Must-follow rule 7):

```bash
# in .worktrees/<slug>/monkify-meta (on feature/<slug>)
git -C <workspace-root>/.worktrees/<slug>/monkify-meta add -A
git -C <workspace-root>/.worktrees/<slug>/monkify-meta commit -m "Sync KB for <feature> ahead of shipping."
git -C <workspace-root>/.worktrees/<slug>/monkify-meta push origin feature/<slug>
```

This auto-commit exception is scoped to the **KB sync in `monkify-ship-feature`**. It does **not**
authorize any other unreviewed commit. The KB now rides along in the same branch that Step 8 merges.

### Step 8: Merge feature/<slug> → the target branch (each affected repo)

The **target** is `main` normally, or the card's `releaseBranch` when it names one.

Per `constraints.md` → "Git", the hub stays on `main` and is never switched. Do the merge in a
**short-lived ship worktree**, then push to the target. For **each** affected repo (**including
`monkify-meta`**, so the KB commit from Step 7 lands too):

```bash
git -C <workspace-root>/<repo> fetch origin

# TARGET = main, or the card's release branch.
# On a release train, create the release branch from origin/main the FIRST time a card ships into it
# — it must branch from main, never from a feature branch, or it inherits work nobody reviewed:
git -C <workspace-root>/<repo> ls-remote --heads origin <target>          # exists?
# if absent and <target> is a release branch:
git -C <workspace-root>/<repo> push origin origin/main:refs/heads/<target>

git -C <workspace-root>/<repo> worktree add ../.worktrees/ship-<slug>/<repo> -b ship-<slug> origin/<target>
git -C <workspace-root>/.worktrees/ship-<slug>/<repo> merge --no-edit origin/<branch>
git -C <workspace-root>/.worktrees/ship-<slug>/<repo> push origin HEAD:<target>

# keep the hub current ONLY when the target was main (the hub is a main hub):
git -C <workspace-root>/<repo> fetch origin && git -C <workspace-root>/<repo> merge --ff-only origin/main
```

(The ship worktree uses a throwaway `ship-<slug>` branch pointed at `origin/<target>` and pushes via
`HEAD:<target>`, so no branch is ever checked out in two places. `ship-<slug>` is deleted in Step 10.)

⚠️ **A repo with nothing to merge still needs the release branch to exist** before a later card tries
to merge into it there. Create it from `origin/main` even when this card touches no code in that repo,
so the train has the same shape in every affected repo.

**Register the merge on the card** — once, before merging, `add_comment`:

```markdown
🌳 Shipping (YYYY-MM-DD) — merging <branch> → <target> from `.worktrees/ship-<slug>/<repo>` (per repo)
```

**On merge conflict:** stop immediately. Report the repo and conflicting files; do **not** continue to
other repos until the user resolves conflicts (or asks you to abort). Because the KB was committed on
the feature branch in Step 7, a `monkify-meta` conflict here means `main` moved under it — resolve in
the ship worktree.

**Hard rules:**

- The hub stays on `main` — never `git checkout` / `git switch` to a feature/ship branch there; the
  ship worktree does the merge; the hub only fetches + `merge --ff-only`.
- Never `git push --force`, never reset `--hard`, never skip hooks, never update git config.
- Keep worktrees under the workspace-root `.worktrees/` dir (untracked by any repo).
- Push each merge result to `origin/main` right after a successful merge.

After all merges succeed, summarize per repo: commits merged, notable files touched.

### Step 9: Trello — Shipped, or held on the train

**If the card named NO release branch** (the normal case), using `cardId`:

1. `get_lists` → capture the **`Shipped`** list id. If no list is named exactly `Shipped`, ask the
   user which list to use (the pipeline's final list). Do not silently fall back to `Implemented`.
2. `move_card` with `cardId`, the **`Shipped`** list id, and `boardId`.
3. Optionally `add_comment` with a one-line ship note (branch merged, spec file, date).

**If the card named a release branch**, do **NOT** move it. Its code is on the release branch and
`main` has never seen it, so calling it Shipped would be false. Leave it in **Implemented** and
`add_comment` instead — this comment is the only thing distinguishing "done, waiting on the train"
from "not started", so it is not optional:

```markdown
🚉 **On the release train (YYYY-MM-DD)** — `<branch>` merged into **`<releaseBranch>`** in <repos>.
**Not on `main` yet**, which is why this card stays in Implemented.

Ships when someone runs `/monkify-ship-feature <releaseBranch>`, which merges the whole train into
`main` and moves every card on it to Shipped.
```

Do **not** rename the card — only move it to **Shipped**. Preserve the title and description.

### Step 10: Clean up — remove worktrees and delete the merged branch

⚠️ **On a release train, delete ONLY the feature branch.** The release branch is still accumulating
cards and must survive; deleting it would strand every card already merged into it, with their work
reachable only through the reflog. The `ship-<slug>` worktree and throwaway branch are still removed
as usual.


The feature branch (incl. its KB commit) is now on `main` in every affected repo, so its worktree(s)
and the branch are redundant. For **each** affected repo, run from `<workspace-root>/<repo>` (its
`main` hub — never from inside a worktree you're about to remove):

```bash
# 1) Remove the feature worktree and the ship worktree.
git -C <workspace-root>/<repo> worktree remove ../.worktrees/<slug>/<repo>
git -C <workspace-root>/<repo> worktree remove ../.worktrees/ship-<slug>/<repo>
git -C <workspace-root>/<repo> worktree prune

# 2) Delete the throwaway ship branch, the merged feature branch (local), then the feature branch on origin.
git -C <workspace-root>/<repo> branch -d ship-<slug>          # throwaway; safe once its commit is on main
git -C <workspace-root>/<repo> branch -d feature/<slug>       # -d only (safe): fails if not fully merged
git -C <workspace-root>/<repo> push origin --delete feature/<slug>
```

Then `add_comment` on the card:

```markdown
🌳 Worktree retired (YYYY-MM-DD) — `feature/<slug>` merged to main in all repos; feature + ship worktrees removed, branch deleted.
```

**Clear the deployed-stack marker if it points at this feature.** If
`<workspace-root>/.worktrees/.deployed-stack.json` exists and references the shipped feature (its
`label`/`cards` name this card, or its `branches` include `feature/<slug>`), the deployed local env is
now stale — the feature is on `main`. Delete the marker (for a `combined` marker that also lists other
still-in-flight cards, instead remove just this feature's card/branch entries). The running containers,
if any, still hold the pre-merge code; mention in the report that a fresh
`/monkify-implement-feature` deploy (or `setup_local_environment.bat` from `main`) will rebuild from
current `main`.

**Hard rules / safety:**

- Use `git branch -d` (merge-checked), **never** `-D` — if `-d` refuses, the branch is **not** fully
  merged: **stop** and report instead of force-deleting.
- Only `git worktree remove` a **clean** worktree; if it has uncommitted changes, **stop** and ask
  (do not `--force`).
- Delete the remote branch only **after** its merge to `main` is pushed. If a repo in the plan did
  not merge, leave its branch alone.
- If the card's `**Worktree:**` path differs from the `.worktrees/<slug>` convention, use the
  registered path.

### Step 11: Report

Tell the user:

1. **Card** — name + link; branch, spec, worktree read from the **Implementation** section; moved to
   **Shipped**.
2. **Verified** — backend unit + integration test result (run on the feature branch before merge).
3. **KB synced** — files changed and the headline business-rule changes; committed on `feature/<slug>`
   **before** the merge, so it landed on `main` with the feature. Note the card's **Level**: if
   `Feature`, confirm the dedicated `features/<slug>.md` was created; if `Minor change`, note that
   **no dedicated `features/<slug>.md` was created** (level-based feature-doc exemption) and which
   existing KB docs were updated instead.
4. **Merged** — `<branch>` → `main` per repo (from a ship worktree, **pushed** to `origin`).
5. **Drift** — any code vs spec vs Trello mismatches worth a follow-up.
6. **Cleanup** — feature + ship worktrees removed; `feature/<slug>` deleted (local + origin) per repo,
   or note what was left in place and why (e.g. a branch that would not `-d`-delete); deployed-stack
   marker cleared if it pointed at this feature (note if a local env was left running with pre-merge
   code).
7. **Next** — deploy or manual QA, only if relevant.

## Hard rules

- **Ask the answer mode before your first question to the user** (`AGENTS.md` rule 9 /
  `constraints.md` §7.0): *short* (question + options + recommendation) or *complete* (each option
  with today's behavior and its trade-offs). Ask it once per run, before the Step 3 ship-plan
  confirmation, and reuse it for every later question; unknown/unanswered ⇒ **complete**. If the mode
  was already chosen earlier in the same chat, reuse it — don't re-ask.
- **KB first, then merge** — sync and commit the KB on `feature/<slug>` **before** merging, so it
  lands on `main` with the feature (not as a separate main commit).
- **The card's `Level` drives the `features/<slug>.md` doc** (read from the `## Implementation`
  block): `Feature` → the dedicated feature doc is required; `Minor change` → exempt (but every
  existing affected `features/*.md` and the rest of the KB must still be kept current). The
  classification comes from refinement, not an ad-hoc call here.
- **Confirm before merge** — never merge without explicit user approval of the plan in Step 3.
- **A `**Release branch:**` on the card changes the target, the card move, and the cleanup — nothing
  else.** The feature merges into that branch instead of `main`, the card **stays in Implemented**
  (its code is not on `main`, so calling it Shipped would be false), and the release branch is
  **never** deleted while cards are still joining it. Absent — the normal case — is unchanged
  behaviour: merge to `main`, move to Shipped.
- **A release branch is cut from `origin/main`, never from a feature branch**, or it silently inherits
  work nobody reviewed.
- **Never force-push a release branch.** It is long-lived and shared across cards; if `main` has moved
  under it, resolve the merge in the ship worktree.
- **Stop on conflict or test failure** — a half-shipped feature or docs that contradict failing tests
  are worse than stopping.
- Honor `constraints.md` — vague operator messaging, mock-only secrets, **worktree-first (the hub
  stays on `main`; merge in a ship worktree)**.
- **Code on the branch wins** for runtime behavior; the spec wins for *design intent*; the **Trello
  card** captures refinement decisions — reconcile all three in the digest.
- This skill does **not** implement features or write specs — use `monkify-implement-feature` and
  `monkify-create-spec` earlier in the pipeline. **Branch and spec come from the Trello card.**

## Step R — form B: land the release train (`/monkify-ship-feature release/<name>`)

This is the second half of a release train: every card has merged into `release/<name>`, and now the
whole thing goes to `main` at once.

**R1 — find the cards on the train.** Search the board for cards whose description contains
`**Release branch:** release/<name>` (check **Implemented** first, then every open list — a card may
have been moved by hand). Report the list you found; if it is empty, **stop** — merging a train with no
cards means the release branch was created by something other than this skill and you do not know what
is on it.

**R2 — confirm the plan.** Present, and wait:

> Land `release/<name>` → `main` in: [repos]. Cards shipping: [N cards, titled + linked].
> Commits per repo: [counts]. After this, all N cards move to **Shipped**.

**R3 — verify the release branch, not the individual features.** The whole point of a train is that
the cards are only coherent together, so the tests that matter are the ones run on the **merged**
result. In a worktree checked out to `origin/release/<name>`:

- `monkify-backend`: `scripts/run_tests.bat` **and** `scripts/run_integration_tests.bat` — both green.
- `monkify-front`: `yarn lint` and `yarn build` — both clean.

⚠️ Green on each feature branch separately does **not** substitute for this. Two cards can each pass
alone and conflict once merged; catching that here is the reason the train exists.

**R4 — merge to `main`.** For each repo that has the branch, same worktree discipline as Step 8:

```bash
git -C <workspace-root>/<repo> fetch origin
git -C <workspace-root>/<repo> ls-remote --heads origin release/<name>   # skip repos without it
git -C <workspace-root>/<repo> worktree add ../.worktrees/ship-release-<name>/<repo> -b ship-release-<name> origin/main
git -C <workspace-root>/.worktrees/ship-release-<name>/<repo> merge --no-edit origin/release/<name>
git -C <workspace-root>/.worktrees/ship-release-<name>/<repo> push origin HEAD:main
git -C <workspace-root>/<repo> fetch origin && git -C <workspace-root>/<repo> merge --ff-only origin/main
```

**On conflict:** stop and report. `main` has moved under a long-lived release branch — resolve in the
ship worktree, never by force-pushing the release branch.

**R5 — Trello.** Move **every** card found in R1 to **Shipped**, and comment on each with the release
branch, the merge date, and the repos. Also comment on the **umbrella card** if the train has one.

**R6 — clean up.** Remove the `ship-release-<name>` worktrees and delete their throwaway branches, then
delete `release/<name>` **local and origin** — the train has arrived and the branch is now a
duplicate of `main`. Clear the deployed-stack marker if it named the release.

**R7 — report.** Per repo: commits merged and files touched. Then the cards shipped, and what is now on
`main` that was not before.

## Trello tools used (server `trello` / `user-trello`)

`list_boards`, `set_active_board`, `get_lists`, `get_cards_by_list_id`, `get_card`,
`get_card_comments`, `get_acceptance_criteria`, `move_card`, `add_comment`.

## References

- `../monkify-create-spec/references/spec-card-metadata.md` — **Implementation** block format on the card.
- `references/knowledge-update-checklist.md` — which KB files to update and what to pull from each.
- `references/feature-doc-template.md` — structure for `features/<name>.md` after shipping.
