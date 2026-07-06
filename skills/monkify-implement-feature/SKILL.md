---
name: monkify-implement-feature
description: Implement a Monkify feature end to end from its spec, in the feature worktree/branch that monkify-create-spec created. Use when the user names a Specs-created Trello card and wants the code written — after monkify-create-spec, before monkify-ship-feature. Gathers session + card + spec context, writes an implementation plan with the Superpowers writing-plans skill, implements it (with unit + mandatory happy-path integration tests), has the user verify, then commits and moves the card to Implemented. Needs Trello MCP and Superpowers.
disable-model-invocation: true
---

# Monkify — Implement Feature

Turn a **Specs created** Trello card into working code on its `feature/<slug>` branch, in the
isolated worktree(s) `monkify-create-spec` already made. Build an implementation plan first (with the
Superpowers **writing-plans** skill), implement it against the spec, get the user's sign-off after
they test it, then commit and move the card to **Implemented**.

This skill **writes application code and tests**; it does **not** write the spec (that's
`monkify-create-spec`) or merge to `main` (that's `monkify-ship-feature`).

Board: `https://trello.com/b/Yi42y6Xy/monkaify` (short id `Yi42y6Xy`).

## Pipeline position

```
Backlog → Refined → Specs created → Implemented → Shipped
           ↑ refine    ↑ create-spec   ↑ THIS SKILL   ↑ ship-feature
```

## Input (required)

The user names a **Trello card** (exact title or unambiguous substring), e.g.:

> `/monkify-implement-feature Add Free bet mode`

**Branch**, **spec file**, **worktree**, and **repos** are **not** asked separately — read them from
the card's **`## Implementation`** section (written by `/monkify-create-spec`). See
`../monkify-create-spec/references/spec-card-metadata.md`.

If the card has no **Implementation** block or **Branch** line, **stop** and tell the user to run
`/monkify-create-spec` first (or add the block manually).

## Prerequisites (verify FIRST — stop if missing)

1. **Trello MCP** — server `user-trello`. Verify with `list_boards`. If missing, **stop**:
   > "The Trello MCP is not available. Install/enable the `trello` MCP server, then re-run
   > `/monkify-implement-feature`."
2. **Superpowers plugin** — the `writing-plans` and `test-driven-development` skills must be
   available (this skill uses `writing-plans` to plan and TDD to implement). If missing, **stop** and
   tell the user to install Superpowers (https://github.com/obra/superpowers).
3. **Sibling repos** — `monkify-backend`, `monkify-front`, and `monkify-meta` exist next to each
   other (see `AGENTS.md`).
4. **Clean `main` hubs** — every repo you will touch has its shared checkout **clean and on `main`**
   (the permanent hub; you work in the feature worktree, never the hub). If a hub is dirty or on a
   feature branch, **stop** and ask the user to restore it to a clean `main`.

Do not write code until prerequisites pass.

## Workflow

Copy this checklist and track progress:

```
- [ ] Step 0: Verify prerequisites
- [ ] Step 1: Resolve board, find card, read everything + parse Implementation
- [ ] Step 2: Load context (INDEX, constraints, spec, features, conversation)
- [ ] Step 3: Ensure the feature worktree(s) exist (recreate from origin if missing)
- [ ] Step 4: Validate the card is implementation-ready (Specs created + spec on disk)
- [ ] Step 5: Write the implementation plan (Superpowers writing-plans)
- [ ] Step 6: Implement the feature in the worktree(s), TDD
- [ ] Step 7: Add the mandatory happy-path integration test (+ one per complex flow)
- [ ] Step 8: Verify — run unit + integration tests
- [ ] Step 9: Hand off to the user to test / review; WAIT for the nod
- [ ] Step 10: Commit + push the work on feature/<slug> (each affected repo)
- [ ] Step 11: Trello — move card to Implemented
- [ ] Step 12: Report
```

### Step 1: Resolve board, find card, read everything + parse Implementation

1. `list_boards` → find `monkaify` (`Yi42y6Xy`); `set_active_board`.
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
5. Verify `monkify-meta/<specPath-relative>` exists on disk. If missing, **stop** and send the user to
   `/monkify-create-spec`.

Keep `cardId`, `branch`, `specPath`, `repos`, and `worktree` for the rest of the workflow.

### Step 2: Load context

Read, in order (follow INDEX.md → "Implementing from a spec"):

1. `monkify-meta/INDEX.md` → `constraints.md`, `architecture.md`, `glossary.md`, `conventions/`.
2. The **target spec** (`specs/<slug>.md`) end to end — data model, API surface, behavior, file
   checklist, unit-test list, acceptance criteria.
3. **Every** `features/*.md` (and skim related shipped specs) so you know what already exists —
   sessions, operators, payments, player identity, free-bet mode, etc. Actively look for
   **cross-feature interactions** (shared entities, headers, payout paths, session/bet lifecycle,
   operator scoping, SignalR contracts, migrations, public API shape).
4. **Conversation context** — when this skill runs in the same chat where the spec was written or the
   feature was discussed, treat the user's confirmed decisions as authoritative alongside the spec;
   don't re-ask what's already answered.

**If you find a conflict, ambiguity, or gap** between the spec and existing behavior that the spec
does **not** resolve — **stop and ask the user** (per INDEX.md). Do not guess or paper over it.

### Step 3: Ensure the feature worktree(s) exist

The feature's work lives in `<workspace-root>/.worktrees/<slug>/<repo>` on `feature/<slug>` (created
by `monkify-create-spec`). Per `constraints.md` → "Git", **never** switch the hub's branch — work in
the worktree.

For **each** affected repo, check whether the worktree is present:

```bash
git -C <workspace-root>/<repo> worktree list        # is .worktrees/<slug>/<repo> there?
```

- **Present** → use it.
- **Missing** (fresh session / cleaned up) → recreate it from the pushed branch, after a collision
  pre-flight (`git fetch origin`; hub must be clean-on-`main`):

  ```bash
  git -C <workspace-root>/<repo> fetch origin
  git -C <workspace-root>/<repo> worktree add ../.worktrees/<slug>/<repo> feature/<slug>
  # if the local branch is absent, track origin:
  # git -C <workspace-root>/<repo> worktree add ../.worktrees/<slug>/<repo> -b feature/<slug> origin/feature/<slug>
  ```

  Then `add_comment` on the card to re-register it (see spec-card-metadata → "Worktree registration"):

  ```markdown
  🌳 Worktree registered (YYYY-MM-DD) — <repo> `feature/<slug>` → `.worktrees/<slug>/<repo>`
  ```

Do all implementation **inside** these worktrees. Never `git checkout -b` / `git switch` in the hub.

### Step 4: Validate the card is implementation-ready

**Stop** and send the user to `/monkify-create-spec` if:

- The card is not in **Specs created** (or later), **or**
- The spec file named in the Implementation block does not exist on disk.

If the feature is clearly already implemented (branch already has the code, card past Specs created),
ask the user whether to continue/extend or skip to reporting.

### Step 5: Write the implementation plan

**REQUIRED SUB-SKILL:** Use superpowers:writing-plans to produce the implementation plan.

Feed the plan the context gathered in Steps 1–2: the **spec** (authoritative design), the **Trello
card** (product intent, acceptance criteria, out-of-scope), the **conversation** decisions, and the
**code touchpoints** the spec names. The plan must cover, per affected repo:

- Backend (`monkify-backend`): domain entities/EF mapping (inline in `OnModelCreating`), migrations
  (added under `Monkify.Infrastructure/Migrations`, applied only by `Monkify.Consumers`), MediatR
  handlers, MassTransit consumers, API controllers/DTOs, SignalR contracts.
- Frontend (`monkify-front`): composables (`src/hooks`), services (`src/services`), types
  (`src/types`), components, `VUE_APP_*` env vars.
- **Tests**: unit tests in `Monkify.Tests` mapped to acceptance criteria, **plus** the mandatory
  happy-path integration test in `Monkify.Tests.Integration` (Step 7).
- The spec's **file checklist** — every file to add/edit.

Save the plan where writing-plans puts it. Keep it aligned with the spec — if planning surfaces a
spec gap or cross-feature conflict, **stop and ask** (Step 2), don't invent behavior.

### Step 6: Implement the feature (TDD, in the worktree(s))

**REQUIRED SUB-SKILL:** Use superpowers:test-driven-development — write the failing test, then the
code, for each behavior. Work through the plan task by task, inside the feature worktree(s).

Honor `constraints.md` throughout:

- **Mock secrets only** — never real credentials in `appsettings*.json` / `.env*`.
- **EF inline** in `MonkifyDbContext.OnModelCreating`; **no** `IEntityTypeConfiguration` classes.
  New migrations via `dotnet ef migrations add`; never hand-edit the model snapshot.
- **Vague operator messaging** — expired/inactive operators get only a generic "not found /
  unavailable" message; never reveal contract status.
- **Host split** — API stays stateless and does not migrate; money/typer/background work stays in
  `Monkify.Consumers`.
- Update the **affected unit tests** so they pass and assert intended behavior; **add** tests for new
  behavior (`Monkify.Tests`).

Follow `references/implementation-checklist.md` for the per-repo quality bar.

### Step 7: Add the mandatory happy-path integration test

Per `constraints.md` → "Integration tests are mandatory for every new feature": a **new feature**
must ship with **at least one** `Monkify.Tests.Integration` (Testcontainers) test that drives the
happy path **end to end** and asserts it works 100% (real API + Consumers hosts against real
Postgres/RabbitMQ/Redis, following the `SessionFlowTests` pattern — not a mock-only unit test). If the
feature introduces a **particularly complex flow** (multi-step transition, refund/rollback,
fail-closed branch), add a **dedicated** integration test for it too.

> **Exemption:** bug fixes, QOL / dev-experience tweaks, and minor changes do **not** require a new
> integration test (they still follow the unit-test discipline). If the card is one of those and the
> user agrees it isn't a "feature", note that in the report and skip this step.

See the mandatory-integration-test memory / the `dev-qol-stuff` feature for how the Testcontainers
harness (RG operator, tokens, SignalR) is extended.

### Step 8: Verify — run the tests

From `monkify-backend` **in its worktree**:

1. **Unit tests** — `scripts/run_tests.bat` (this excludes the slow `ProductionSessions` Monte Carlo
   suite; never run the full `dotnet test` for routine verification).
2. **Integration tests** — `scripts/run_integration_tests.bat` (Docker must be running).

If any suite fails, **fix it** (loop back to Step 6) — do not proceed to hand-off with red tests.
Record which unit + integration tests were added/changed (for the report and for `constraints.md`
§3 "report which unit tests changed").

Frontend: run the app's checks if the feature touched the UI (build / type-check / lint as the repo
provides). Manually reason through the composable/service/type changes against the spec.

### Step 9: Hand off to the user to test / review — WAIT for the nod

Tell the user the feature is implemented and green, and **ask them to test it / confirm it looks
good** before anything is committed. Give them what they need to try it:

- What changed (per repo, short bullets) and which acceptance criteria are now satisfied.
- How to run it locally if useful — e.g. `monkify-backend/scripts/setup_local_environment.bat`, or
  point them to `/monkify-test-features <card>` to spin up a throwaway test environment.
- Any manual QA steps from the spec's **Manual QA** section.

**Do not commit or move the card until the user gives an explicit go-ahead.** If they report
problems, loop back to Step 6, fix, re-verify (Step 8), and hand off again.

### Step 10: Commit + push the work (each affected repo)

Once the user approves, commit the implementation **in each affected repo's worktree** (never the
hub) and push the feature branch:

```bash
# inside .worktrees/<slug>/<repo>
git -C <workspace-root>/.worktrees/<slug>/<repo> status
git -C <workspace-root>/.worktrees/<slug>/<repo> add -A
git -C <workspace-root>/.worktrees/<slug>/<repo> commit -m "Implement <short feature title> (<repo>)."
git -C <workspace-root>/.worktrees/<slug>/<repo> push origin feature/<slug>
```

**Hard rules:**

- Commit only this feature's work; leave unrelated local changes alone.
- Never commit on `main`; never `git push --force`; never skip hooks; never update git config.
- Never commit real secrets/config values.
- Push each branch to `origin` right after committing (see `AGENTS.md` → push after branch work).
- If a hook rejects the commit, fix the cause and make a **new** commit — don't amend to bypass hooks.

Do **not** merge to `main` here — that's `monkify-ship-feature`.

### Step 11: Trello — move card to Implemented

Using `cardId`:

1. `get_lists` → capture the **`Implemented`** list id. If no list is named exactly `Implemented`,
   ask the user which list to use.
2. `move_card` with `cardId`, the **`Implemented`** list id, and `boardId`.
3. Optionally `add_comment` with a one-line implementation note (branch, tests added, date).

Do **not** rename the card or edit its title/description beyond an optional status note.

### Step 12: Report

Tell the user:

1. **Card** — name + link; moved to **Implemented**; branch/spec/worktree read from the
   **Implementation** section.
2. **Plan** — where the writing-plans plan lives.
3. **Implemented** — per-repo summary of what was built; acceptance criteria covered.
4. **Tests** — **exactly which unit tests** were added/changed (per `constraints.md` §3) and the
   integration test(s) added; confirm both suites are green (or note the integration-test exemption
   if this was a fix/QOL change).
5. **Committed + pushed** — `feature/<slug>` in which repos.
6. **Open items** — anything deferred, any spec gap the user resolved mid-implementation.
7. **Next** — run `/monkify-ship-feature <card name>` to sync the KB and merge to `main`.

## Hard rules

- **Plan before code** — use writing-plans first; implement against the spec, not from memory.
- **Stop on spec/cross-feature conflict** — ask the user; never guess (INDEX.md).
- **User verifies before commit** — no commit, no card move, until the user gives the nod.
- **Every new feature gets a happy-path integration test** (and one per complex flow) — see
  `constraints.md`. Fixes/QOL/minor changes are exempt.
- **Worktree-first** — implement in `.worktrees/<slug>/<repo>`; the hub stays on `main`.
- Honor `constraints.md` — mock secrets, EF inline, migrations via Consumers, vague operator
  messaging, host split.
- **No merge to `main`** — that's `monkify-ship-feature`.

## Trello tools used (server `trello` / `user-trello`)

`list_boards`, `set_active_board`, `get_lists`, `get_cards_by_list_id`, `get_card`,
`get_card_comments`, `get_acceptance_criteria`, `move_card`, `add_comment` (optional/registration).

## References

- `references/implementation-checklist.md` — per-repo quality bar for the implementation.
- `../monkify-create-spec/references/spec-card-metadata.md` — the **Implementation** block format on
  the card (source of branch / spec / worktree / repos).
