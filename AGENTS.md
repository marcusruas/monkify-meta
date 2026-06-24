# AGENTS.md — Monkify

Guidance for AI agents working on Monkify (`monkify-backend`, `monkify-front`, and this
`monkify-meta` knowledge base). This file lives in the knowledge base; paths below are relative to
the `monkify-meta` repo root unless a repo is named explicitly.

## Workspace setup — bootstrap the root pointer (do this first)

This file is only auto-loaded by Cursor when working **inside `monkify-meta/`**. To make these
rules apply across the whole workspace (so they're also active in `monkify-backend/` and
`monkify-front/`), there must be a pointer `AGENTS.md` at the **workspace root** — the parent
folder that contains the `monkify-meta`, `monkify-backend`, and `monkify-front` folders.

On your first task in this workspace, ensure that pointer exists:

1. Determine the workspace root (the parent directory of this `monkify-meta` repo).
2. Look at `<workspace-root>/AGENTS.md`.
3. **If it does not exist, OR it does not contain the marker `<!-- monkify-agents-pointer -->`,**
   create or update it so it contains the pointer block below. Preserve any other existing content
   in that file; just add the block if it's missing.
4. Otherwise (marker already present), do nothing.

Pointer block to write at `<workspace-root>/AGENTS.md`:

```markdown
<!-- monkify-agents-pointer -->
# AGENTS.md — Monkify workspace

Before any task in this workspace, read `monkify-meta/AGENTS.md` and `monkify-meta/INDEX.md`.
They hold the must-follow rules and the project knowledge base map that apply to every repo
(`monkify-backend`, `monkify-front`, `monkify-meta`).
```

> Note: the workspace root is not one of the git repos, so this pointer is intentionally not
> version-controlled — it is (re)generated from this file. `monkify-meta/AGENTS.md` remains the
> single source of truth; keep the real rules here, not in the pointer.

## Must-follow rules

1. **Read `INDEX.md` before any task.** It is the map of the project knowledge base; it points to
   `architecture.md`, `constraints.md`, `glossary.md`, the `conventions/`, and the `features/` docs.
   Read `constraints.md` too — those rules are non-negotiable.
2. **Implementation specs live in `specs/`.** Treat them as the authoritative design for in-flight
   features and **follow their format** when writing or updating specs.
3. **Backend changes:** when you change behavior in `monkify-backend`, **run `Monkify.Tests`**, fix
   the affected tests, add tests for new behavior, and **report which unit tests changed** at the end.
4. **Multi-repo work:** create a feature branch in **EACH affected repository**
   (`monkify-backend` and `monkify-front`) — never commit a cross-repo feature to a single repo only.
   Use the agreed feature name (e.g. `feature/operators`, `feature/payment-methods`).

## Quick context

- **monkify-backend** — .NET 8: `Monkify.Api` (stateless HTTP API + SignalR) and
  `Monkify.Consumers` (background workers, the game loop, payouts, EF migrations). Shared:
  `Monkify.Domain`, `Monkify.Infrastructure`, `Monkify.Common`. Tests: `Monkify.Tests`.
- **monkify-front** — Vue 3 + TypeScript SPA (composables in `src/hooks`, services in
  `src/services`, types in `src/types`).
- **monkify-meta** — the knowledge base (this guidance's source of truth).

## Other constraints (see `constraints.md` for the full list)

- **Secrets/config files:** you may edit them, but only ever write **mock/placeholder** values —
  never real credentials, and never print/commit real secrets.
- **EF config is inline** in `MonkifyDbContext.OnModelCreating`; **migrations are applied only by
  `Monkify.Consumers`** at startup.
- **Do not run production queries or external/Unidas API requests** that could expose sensitive
  data — ask the user to run them.
- For expired/inactive operators, only ever surface a **vague** "not found / unavailable" message.
