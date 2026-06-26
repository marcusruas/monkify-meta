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
- [ ] Step 5: Write the refined result back to the card
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
- Constraints from `constraints.md` that apply (secrets, branch-per-repo, unit tests, vague
  operator messaging).

**Ask, don't assume.** Whenever the card lacks instructions for how it behaves in a particular
feature, ask a concrete scenario question, e.g.:
> "In scenario X, when <event> happens while <feature> is active, how should this behave?"

Keep asking until there are **no** open ambiguities a spec-writer would have to guess about.
The exit bar: the card now contains enough detail to write an implementation spec for an AI agent
**without further clarification**.

### Step 5: Write the refined result back to the card

When the user confirms refinement is complete:

1. Update the description via `update_card_details` using the structure in
   `references/card-template.md` (Problem, Refinement notes/decisions, Backend impact, Frontend
   impact, Edge cases, Out of scope, Affected repos, Constraints). Preserve any original intent;
   integrate, don't blindly overwrite history.
2. Acceptance criteria → the **"Acceptance Criteria"** checklist (so `get_acceptance_criteria`
   finds them later). If the checklist does not exist, `create_checklist`
   (name: `Acceptance Criteria`); add each criterion with `add_checklist_item`
   (`checkListName: "Acceptance Criteria"`, scoped by `cardId`). Each item must be testable and use
   Given / When / Then phrasing.
3. Optionally `add_comment` with a short changelog of the key decisions reached during refinement.

### Step 6: Move the card to "Refined"

`move_card` with the `cardId` and the **`Refined`** list id (and `boardId`).

### Step 7: Report

Tell the user: the card name + link, what changed (description, N acceptance criteria), the
decisions captured, and confirm it was moved to **Refined** and is ready for a spec via
`/monkify-create-spec`.

## Trello tools used (server `trello` / `user-trello`)

`list_boards`, `set_active_board`, `get_lists`, `get_cards_by_list_id`, `get_card`,
`get_card_comments`, `get_acceptance_criteria`, `update_card_details`, `create_checklist`,
`add_checklist_item`, `add_comment`, `move_card`.

## Hard rules

- **Do not** write a spec or any code in this skill — refinement only. (Spec writing is a separate,
  later step.)
- **Do not** move the card to `Refined` until the user confirms refinement is complete.
- Honor `constraints.md` — never surface real secrets; never reveal operator contract status in
  user-facing copy; respect branch-per-repo and unit-test expectations when describing impact.

## References

- `references/refinement-checklist.md` — the Monkify-wide coverage checklist to drive questions.
- `references/card-template.md` — the description structure to write back to the card.
