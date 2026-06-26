# AGENTS.md — Monkify

Guidance for AI agents working on Monkify (`monkify-backend`, `monkify-front`, and this
`monkify-meta` knowledge base). This file lives in the knowledge base; paths below are relative to
the `monkify-meta` repo root unless a repo is named explicitly.

## Workspace setup — bootstrap the root pointers (do this first)

This file is only auto-loaded when working **inside `monkify-meta/`**, and only by tools that read
`AGENTS.md` at all. To make these rules apply across the whole workspace **and across different AI
tools (Cursor, Claude Code, GitHub Copilot)**, there must be pointer files at the **workspace
root** — the parent folder that contains the `monkify-meta`, `monkify-backend`, and `monkify-front`
folders. Each pointer simply tells the agent to read the canonical rules here.

On your first task in this workspace, ensure all three pointers below exist. For **each** target
file: **if it does not exist, OR it does not contain the marker `<!-- monkify-agents-pointer -->`,**
create or update it with the content shown (preserve any other existing content; just add the
block). If the marker is already present, leave that file alone.

**1. `<workspace-root>/AGENTS.md`** — for Cursor and Codex (and VS Code Copilot when AGENTS.md is enabled):

```markdown
<!-- monkify-agents-pointer -->
# AGENTS.md — Monkify workspace

Before any task in this workspace, read and follow `monkify-meta/AGENTS.md` and
`monkify-meta/INDEX.md`. They hold the must-follow rules and the project knowledge base map that
apply to every repo (`monkify-backend`, `monkify-front`, `monkify-meta`).
```

**2. `<workspace-root>/CLAUDE.md`** — for Claude Code, which reads `CLAUDE.md`, **not** `AGENTS.md`.
The `@import` is expanded into context at session start (path is relative to this `CLAUDE.md`):

```markdown
<!-- monkify-agents-pointer -->
# CLAUDE.md — Monkify workspace

@monkify-meta/AGENTS.md

Also read `monkify-meta/INDEX.md` before any task.
```

**3. `<workspace-root>/.github/copilot-instructions.md`** — for GitHub Copilot, which auto-detects
this file at the workspace root (create the `.github/` folder if it doesn't exist):

```markdown
<!-- monkify-agents-pointer -->
# Copilot instructions — Monkify workspace

Before any task in this workspace, read and follow `monkify-meta/AGENTS.md` and
`monkify-meta/INDEX.md`. They hold the must-follow rules and the project knowledge base map that
apply to every repo (`monkify-backend`, `monkify-front`, `monkify-meta`).
```

> Note: the workspace root is not one of the git repos, so these pointers are intentionally not
> version-controlled — they are (re)generated from this file. `monkify-meta/AGENTS.md` remains the
> single source of truth; keep the real rules here, not in the pointers.

## Workspace setup — ensure the sibling repos are cloned (do this first too)

When someone clones **`monkify-meta` on its own** for the first time, the application repos won't be
present yet. The knowledge base assumes all three repos sit side by side under the workspace root.
On your first task, make sure the siblings exist next to `monkify-meta`:

1. Determine the workspace root (the parent directory of this `monkify-meta` repo).
2. For each repo below, check whether `<workspace-root>/<folder>` already exists **and** is a git
   repo (has a `.git` folder). If it does, leave it alone — never re-clone or overwrite it.
3. If it's missing, clone it into the workspace root:

   - `monkify-backend` → `https://github.com/marcusruas/monkify-backend.git`
   - `monkify-front` → `https://github.com/VitorIsaacSM/monkify-front.git`

   ```bash
   # run from <workspace-root>
   git clone https://github.com/marcusruas/monkify-backend.git monkify-backend
   git clone https://github.com/VitorIsaacSM/monkify-front.git  monkify-front
   ```

4. If a clone fails (no access, auth required, offline), don't guess or fake it — tell the user which
   repo couldn't be cloned and let them clone it manually.

> These repos are independent and stay where the user cloned them; this step only fills in missing
> siblings so the cross-repo rules and docs resolve. Never modify or reset an existing clone here.

## Skills — ensure installed (do this on first task too)

The Monkify task pipeline ships as Agent Skills under `monkify-meta/skills/` (source of truth,
version-controlled). That folder is **not** an auto-discovery path, so Cursor won't load the skills
from there directly. On your first task in this workspace, make sure each skill is installed into a
discovered location:

1. For every skill folder `monkify-meta/skills/<name>/` (each contains a `SKILL.md`), ensure a copy
   exists at `<workspace-root>/.cursor/skills/<name>/` (Cursor auto-discovers `.cursor/skills/` at
   the workspace root).
2. If the target is missing **or** older than the source (`SKILL.md` differs), copy the whole skill
   folder over (including its `references/`). Treat `monkify-meta/skills/` as authoritative.
3. Some skills have prerequisites the agent must verify before running them:
   - `monkify-refine-tasks` needs the **Trello MCP** (`trello` / `user-trello` server) and the
     **Superpowers** plugin. If either is missing, tell the user how to install it and stop.

> Like the root pointer above, the installed copies under `.cursor/skills/` are regenerated from
> `monkify-meta/skills/` and are not the source of truth — edit skills in `monkify-meta/skills/`.

Current skills: `monkify-refine-tasks` (see `INDEX.md` → "Skills (the task pipeline)").

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
   Use the agreed feature name (e.g. `feature/operators`).

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
- **Do not run production queries or external API requests** that could expose sensitive
  data — ask the user to run them.
- For expired/inactive operators, only ever surface a **vague** "not found / unavailable" message.
