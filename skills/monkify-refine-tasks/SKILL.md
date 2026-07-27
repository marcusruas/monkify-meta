---
name: monkify-refine-tasks
description: Refine a Monkify task from the Trello board into a clear, unambiguous, spec-ready work item. Use when the user wants to pick, refine, groom, or clarify a Monkify Trello card before a spec is written — covering backend (monkify-backend) and frontend (monkify-front) features, edge cases, and acceptance criteria. Reads the monkify-meta INDEX, pulls the card from the "monkaify" board, refines it with the user, writes the result back to the card, and moves it to the "Refined" list.
disable-model-invocation: true
---

# Monkify — Refine Tasks

Turn a rough Trello card on the **monkaify** board into a fully-refined work item that contains
everything needed to later generate an implementation spec for an AI agent. This skill **does not
write the spec or any code** — it produces a card a spec-writer can consume without guessing.

Board: `https://trello.com/b/Yi42y6Xy/monkaify` (short id `Yi42y6Xy`, name `monkaify`).

## Prerequisites (verify FIRST — stop if missing)

1. **Trello MCP** — server `user-trello` (serverName `trello`). Verify by listing boards
   (`list_boards`). If the call fails or the server is absent, **stop** and tell the user:
   > "The Trello MCP is not available. Install/enable the `trello` MCP server, then re-run
   > `/monkify-refine-tasks`."
2. **Superpowers plugin** — the `brainstorming` skill must be available (it drives the refinement
   dialogue). If it is not installed, **stop** and tell the user:
   > "The Superpowers plugin isn't installed, which this skill uses for refinement. Install it
   > (https://github.com/obra/superpowers), then re-run `/monkify-refine-tasks`."

Do not proceed past a missing prerequisite.

## Workflow

Copy this checklist and track progress:

```
- [ ] Step 0: Verify prerequisites (Trello MCP + Superpowers)
- [ ] Step 1: Load Monkify context (INDEX + linked docs)
- [ ] Step 2: Resolve the board and pick the card
- [ ] Step 3: Read EVERYTHING on the card
- [ ] Step 4: Refine with the user (Superpowers brainstorming)
- [ ] Step 4b: Classify the level with the user (Feature vs Minor change)
- [ ] Step 5: Write the refined result back to the card
- [ ] Step 5b: Register any git worktree you used on the card
- [ ] Step 6: Move the card to "Refined"
- [ ] Step 7: Report
```

### Step 1: Load Monkify context

Read `monkify-meta/INDEX.md` first, then follow its links as needed: `constraints.md`,
`architecture.md`, `glossary.md`, the `conventions/` files, and **every** file in `features/`.
Refinement must reason about the project as a whole, so know what features exist on **both** the
backend (`monkify-backend`) and frontend (`monkify-front`) before asking questions.

### Step 2: Resolve the board and pick the card

1. `list_boards` → find the board named `monkaify` (short id `Yi42y6Xy`); capture its `boardId`.
2. `set_active_board` with that `boardId`.
3. `get_lists` for the board. Identify the source list (where un-refined tasks live — ask the user
   which list if it is ambiguous) and the **`Refined`** target list. Capture both list ids.
4. If the user named a specific card, find it. Otherwise `get_cards_by_list_id` on the source list
   and ask the user which card to refine. Capture the `cardId`.

### Step 3: Read EVERYTHING on the card

Gather the full current state so nothing is lost or duplicated:

- `get_card` (with `includeMarkdown: true`) — name + description.
- `get_card_comments` — prior discussion / decisions.
- `get_acceptance_criteria` — existing acceptance-criteria checklist items, if any.

Summarize the card back to the user in 2-4 sentences before refining.

### Step 4: Refine with the user (the core of this skill)

Use the Superpowers **brainstorming** skill to drive the dialogue (one question at a time,
multiple-choice when possible). The goal is a task that is **clear, concise, and unambiguous**.

Refinement MUST be Monkify-wide. Walk `references/refinement-checklist.md` and, for the card at
hand, cover impact across **all** relevant features and both apps:

- Which features does this touch or interact with? (sessions/typer, bets, payments/payment methods,
  operators/multi-tenancy, rewards & refunds, money ledger, SignalR streaming, scheduling.)
- Backend (`monkify-backend`) impact: domain entities, handlers, consumers, EF/migrations, payouts.
- Frontend (`monkify-front`) impact: composables, services, types, live UI.
- Edge cases, failure modes, and concurrency (e.g. mid-session changes, operator contract expiry,
  payment failure/refund, free-coin vs paid, header present vs absent).
- Constraints from `constraints.md` that apply (secrets, worktree-per-repo, unit tests, vague
  operator messaging).

**Ask, don't assume.** Whenever the card lacks instructions for how it behaves in a particular
feature, ask a concrete scenario question, e.g.:
> "In scenario X, when <event> happens while <feature> is active, how should this behave?"

#### Every question carries its trade-offs (required — `AGENTS.md` rule 9 / `constraints.md` §7)

Never ask a bare question. **Each** question you put to the user must come with the trade-offs of the
answers **versus the current functionality**, so the user is deciding with the consequences in front
of them instead of guessing. For every question:

1. State **what happens today** in that area — the current behavior, and where it lives
   (`features/<x>.md`, the spec, or the code). If nothing exists yet, say so.
2. Give the realistic **options** (including **keep it as it is** — always an option).
3. For **each** option, give the trade-offs in **exactly this format**:

   ```
   Benefits: X, Y, Z
   Issues that this approach brings: A, B, C
   ```

4. End with **your recommendation and why**.

Both lines are mandatory on every option. Fill them with concrete items: what the option solves and
for whom, against complexity, blast radius across other features (game/bet lifecycle, payments &
payouts, refunds, operators, SignalR contracts, caching, scheduling), schema/migration impact,
performance, ops burden, the test surface it adds (including the mandatory happy-path integration
test if the card is a `Feature`), and how hard it is to **undo** later.

Worked shape:

```markdown
**Option A — <name>**
Today: <current behavior + where it lives>.
Benefits: <gain>, <gain>
Issues that this approach brings: <cost>, <risk>

**Option B — keep the current behavior**
Today: <same>.
Benefits: <no migration, no new test surface>
Issues that this approach brings: <the problem stays unsolved>

**Recommendation:** <option> — <why>.
```

Scale the depth to the stake — a naming or copy question gets a couple of short items per line;
anything touching money, migrations, or a shipped feature's behavior gets the full treatment. Never
drop the `Issues` line: if an option genuinely has no real downside, write
`Issues that this approach brings: none beyond one extra query` rather than listing only upsides.
Record the chosen answer **and the trade-off that drove it** in the card's
`## Decisions (from refinement)` section (Step 5).

Keep asking until there are **no** open ambiguities a spec-writer would have to guess about.
The exit bar: the card now contains enough detail to write an implementation spec for an AI agent
**without further clarification**.

### Step 4b: Classify the level with the user (Feature vs Minor change)

Before writing the card back, agree on **one** classification with the user. This is a required
refinement output — it decides whether the downstream skills create the **feature artifacts** or
skip them, so don't leave it implicit:

- **`Feature`** — a new capability or a substantive change to existing behavior. A `Feature`
  **requires** the downstream feature artifacts: a **mandatory happy-path integration test** in
  `/monkify-implement-feature`, and a dedicated **`features/<slug>.md`** knowledge-base doc in
  `/monkify-ship-feature`.
- **`Minor change`** — a bug fix, a QOL / dev-experience tweak, or a small change to existing
  behavior. A `Minor change` is **exempt** from both of those new artifacts. It still needs unit
  tests, and it still keeps the KB current — any existing `features/*.md` docs the change affects
  must still be corrected downstream; only the *new dedicated* artifacts are waived.

Ask directly ("Is this a **Feature** or a **Minor change**?") — and, as with every other question,
present the trade-offs in the required format:

```markdown
**Feature**
Benefits: the happy path is verified end to end by a mandatory integration test, the behavior gets a
dedicated `features/<slug>.md` doc so the KB stays true, regressions get caught at ship time.
Issues that this approach brings: slower to ship, an integration test to write and keep green, an
extra KB doc to maintain.

**Minor change**
Benefits: ships faster, no new integration test, no new KB doc — only the existing `features/*.md`
docs get corrected.
Issues that this approach brings: nothing pins the new behavior down, so a later change can silently
break it; if it turns out to be a real capability the artifacts are missing.
```

Confirm the user understands the downstream effect. **When in doubt, classify as `Feature`** — that is the safe default; never skip an
artifact by guessing "minor". Record the agreed level (plus a one-line rationale) in the card's
`## Level` section in Step 5. `/monkify-create-spec` carries it into the `## Implementation` block,
and `/monkify-implement-feature` + `/monkify-ship-feature` read it from there.

### Step 5: Write the refined result back to the card

When the user confirms refinement is complete:

1. Update the description via `update_card_details` using the structure in
   `references/card-template.md` (Problem, Level, Refinement notes/decisions, Backend impact,
   Frontend impact, Edge cases, Out of scope, Affected repos, Constraints). The **`## Level`** section
   records the Step 4b classification (`Feature` or `Minor change`) with its one-line rationale.
   Preserve any original intent; integrate, don't blindly overwrite history.
2. Acceptance criteria → the **"Acceptance Criteria"** checklist (so `get_acceptance_criteria`
   finds them later). If the checklist does not exist, `create_checklist`
   (name: `Acceptance Criteria`); add each criterion with `add_checklist_item`
   (`checkListName: "Acceptance Criteria"`, scoped by `cardId`). Each item must be testable and use
   Given / When / Then phrasing.
3. Optionally `add_comment` with a short changelog of the key decisions reached during refinement.

### Step 5b: Register any git worktree you used on the card

Refinement is normally **Trello + read-only context** (INDEX, docs, code exploration) and creates no
branch, so usually there is **no worktree to register**. But per `AGENTS.md` rule 6, if you do any
work that **touches a repo** you must do it in an **isolated git worktree** at
`.worktrees/<slug>/<repo>` (run the collision pre-flight first; leave the shared checkout on `main` —
never `git checkout -b` there) — and whenever a Monkify skill creates or enters a worktree it must
**register it on the card** (see `../monkify-create-spec/references/spec-card-metadata.md` → "Worktree
registration"):

- If (and only if) you created/entered a worktree during this refinement, `add_comment` on the card:

  ```markdown
  🌳 Worktree registered (YYYY-MM-DD) — <repo> `<branch-or-detached>` → `<worktree-path>`
  ```

- If you stayed read-only (the normal case), state that in the report — no worktree, nothing to
  register.

### Step 6: Move the card to "Refined"

`move_card` with the `cardId` and the **`Refined`** list id (and `boardId`).

### Step 7: Report

Tell the user: the card name + link, what changed (description, N acceptance criteria), the
**Level** it was classified as (`Feature` or `Minor change`) and what that means downstream (whether
the integration test + `features/` doc will be created), the decisions captured, whether a
**worktree** was used (and registered on the card) or the refinement stayed read-only, and confirm it
was moved to **Refined** and is ready for a spec via `/monkify-create-spec`.

## Trello tools used (server `trello` / `user-trello`)

`list_boards`, `set_active_board`, `get_lists`, `get_cards_by_list_id`, `get_card`,
`get_card_comments`, `get_acceptance_criteria`, `update_card_details`, `create_checklist`,
`add_checklist_item`, `add_comment`, `move_card`.

## Hard rules

- **Do not** write a spec or any code in this skill — refinement only. (Spec writing is a separate,
  later step.)
- **Never ask a bare question.** Every question in Step 4 — including the Step 4b Level question —
  must present, for each option (always including "keep it as it is"), today's behavior plus the
  trade-offs in the required format — `Benefits: …` / `Issues that this approach brings: …` — and
  close with your recommendation. Both lines are mandatory; never drop `Issues`. See `AGENTS.md`
  rule 9 / `constraints.md` §7.
- **Always classify the Level** (`Feature` / `Minor change`) with the user and record it in `## Level`
  — it gates whether the integration test and `features/` doc get created downstream. When unsure,
  choose `Feature` (never skip an artifact by guessing "minor").
- **Do not** move the card to `Refined` until the user confirms refinement is complete.
- Honor `constraints.md` — never surface real secrets; never reveal operator contract status in
  user-facing copy; respect worktree-per-repo (hub stays on `main`) and unit-test expectations when
  describing impact.

## References

- `references/refinement-checklist.md` — the Monkify-wide coverage checklist to drive questions.
- `references/card-template.md` — the description structure to write back to the card.
- `../monkify-create-spec/references/spec-card-metadata.md` — worktree-registration convention (all skills).
