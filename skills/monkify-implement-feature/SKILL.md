---
name: monkify-implement-feature
description: Implement a Monkify feature end to end from its spec, in the feature worktree/branch that monkify-create-spec created. Use when the user names a Specs-created Trello card and wants the code written — after monkify-create-spec, before monkify-ship-feature. Gathers session + card + spec context, writes an implementation plan with the Superpowers writing-plans skill, implements it (with unit + mandatory happy-path integration tests), commits & pushes, hands back a GitHub compare link (branch vs main) per repo, deploys the feature to the local Docker stack (setup_local_environment), has the user verify the running containers, then moves the card to Implemented. Needs Trello MCP, Superpowers, and Docker.
disable-model-invocation: true
---

# Monkify — Implement Feature

Turn a **Specs created** Trello card into working code on its `feature/<slug>` branch, in the
isolated worktree(s) `monkify-create-spec` already made. Build an implementation plan first (with the
Superpowers **writing-plans** skill), implement it against the spec, **commit & push**, **deploy the
feature to the local Docker stack** so the user tests the real running containers, get their sign-off,
then move the card to **Implemented**.

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
5. **Docker running** — the integration tests (Step 8) and the deploy (Step 10) both need it. Verify
   `docker info` succeeds; if not, **stop** and ask the user to start Docker.
6. **`impeccable` skill + `claude-in-chrome`** — needed only when the feature has a **visual surface**
   (Step 6b): `impeccable` for the design pass, `claude-in-chrome` to read the card's mockups (the
   `chrome-devtools` profile is not signed in to Trello). If the card touches UI and either is missing,
   **stop** and tell the user, rather than eyeballing the design.

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
- [ ] Step 6b: UI surfaces — read the card's mockups, run the `impeccable` design pass, screenshot-compare
- [ ] Step 7: Add the mandatory happy-path integration test (+ one per complex flow)
- [ ] Step 8: Verify — run unit + integration tests
- [ ] Step 9: Commit + push the work on feature/<slug> (each affected repo)
- [ ] Step 9b: Build the GitHub compare links (feature branch vs main) for every pushed repo
- [ ] Step 10: Deploy the feature to Docker (setup_local_environment) — handle a stack already running
- [ ] Step 11: Hand off to the user to test the running containers; WAIT for the nod
- [ ] Step 12: Trello — move card to Implemented
- [ ] Step 13: Report
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
   - **`level`** — from `**Level:**` (`Feature` or `Minor change`; case-insensitive — anything not
     clearly `Minor change` is treated as `Feature`). If the line is absent (older card), fall back to
     the card's `## Level` section, and if that's missing too, default to **`Feature`**. This drives
     the mandatory-integration-test decision in Step 7.
5. Verify `monkify-meta/<specPath-relative>` exists on disk. If missing, **stop** and send the user to
   `/monkify-create-spec`.

Keep `cardId`, `branch`, `specPath`, `repos`, `worktree`, and `level` for the rest of the workflow.

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

#### Step 6b: UI work goes through the design skill (mandatory when the feature has a visual surface)

If the feature adds or changes anything a player sees — a component, a modal, a page, a panel — you
**must** run the design pass below. Writing frontend markup straight from the spec's prose produces
something that is functionally correct and visually wrong; the spec describes behavior, not design.

1. **Get the mockups before writing markup.** Card attachments are the design source of truth and they
   outrank the spec's prose description of the UI. Read them off the card (`get_card` lists
   `badges.attachments`; the description usually links them). Trello attachment URLs need the user's
   session — the `chrome-devtools` profile is **not** logged in, so open them with **`claude-in-chrome`**
   and screenshot. If there is no mockup, say so and design from `conventions/frontend.md` + the
   existing arcade tokens.
2. **REQUIRED SUB-SKILL: `impeccable`.** Invoke it for the build (`/impeccable craft <surface>`) or, on
   an existing surface, `critique` / `polish`. Its rules on hierarchy, contrast, motion and slop are the
   bar. ⚠️ Its setup step will report **`NO_PRODUCT_MD`** and tell you to run its `init` interview
   first — **do not**. This workspace deliberately keeps no `PRODUCT.md` / `DESIGN.md` (decided
   2026-07-29): the design source is the card's mockups plus the arcade tokens already in
   `colors.css` / `arcade.css`. Apply the skill's design rules directly and carry on.
3. **Reuse the design system, do not reinvent it.** `arcade.css` + `colors.css` tokens, the existing
   `src/components/ui/` primitives, and the `.btn-arcade` rule (use it as-is or build with plain
   Tailwind + tokens — never half-adopt it).
4. **Screenshot-compare before hand-off.** Render the real surface in the browser against the local
   stack and put it side by side with the mockup at desktop **and** phone widths. Fix the differences
   you can see. Do not hand off a UI you have not looked at.

⚠️ **The mockup's *copy* is not automatically the shipped copy.** Refinement decisions on the card
override it (a mockup routinely carries wording the refinement later rejected). Match the mockup's
**design**; follow the card for **words**.

### Step 7: Add the mandatory happy-path integration test

**Gate this step on the card's `level`** (parsed in Step 1):

- **`level == Feature`** → this step is **mandatory**. Per `constraints.md` → "Integration tests are
  mandatory for every new feature", the feature must ship with **at least one**
  `Monkify.Tests.Integration` (Testcontainers) test that drives the happy path **end to end** and
  asserts it works 100% (real API + Consumers hosts against real Postgres/RabbitMQ/Redis, following the
  `SessionFlowTests` pattern — not a mock-only unit test). If the feature introduces a **particularly
  complex flow** (multi-step transition, refund/rollback, fail-closed branch), add a **dedicated**
  integration test for it too.
- **`level == Minor change`** → **exempt.** Bug fixes, QOL / dev-experience tweaks, and minor changes
  do **not** require a new integration test (they still follow the unit-test discipline). Note the
  exemption in the report and skip the rest of this step.

> The `level` comes from refinement (`/monkify-refine-tasks` → `## Level`) via the `## Implementation`
> block, so this is **not** an ad-hoc judgment. If the code you actually wrote clearly contradicts the
> recorded level (e.g. a card marked `Minor change` turned into a substantial new capability), **stop
> and confirm with the user** before skipping the test — don't silently honor a stale classification.

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

### Step 9: Commit + push the work (each affected repo)

Once the tests are green (Step 8), commit the implementation **in each affected repo's worktree**
(never the hub) and push the feature branch. This now happens **before** the user tests — they test
the committed, containerized build (Steps 10–11), not an uncommitted working tree.

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
- If the user later reports problems (Step 11), fix (Step 6), re-verify (Step 8), then make a
  **follow-up** commit here and **redeploy** (Step 10) — never amend the pushed commit.

Do **not** merge to `main` here — that's `monkify-ship-feature`.

### Step 9b: Build the GitHub compare links (branch vs `main`)

Right after the push succeeds, produce — for **every** repo you pushed — a **GitHub compare link**
so the user can review exactly what this feature changed against `main` in the browser. This is a
required output of the skill: the links go into the Step 11 hand-off **and** the Step 13 report.

1. **Confirm the push landed** (never link to a branch that isn't on `origin`):

   ```bash
   git -C <workspace-root>/.worktrees/<slug>/<repo> rev-parse HEAD
   git -C <workspace-root>/.worktrees/<slug>/<repo> ls-remote --heads origin feature/<slug>
   ```

   The two SHAs must match. If the branch is absent or behind, push again before building the link.

2. **Derive the repo's web URL from its own remote** — never hardcode owners (backend and front live
   under different GitHub accounts):

   ```bash
   git -C <workspace-root>/.worktrees/<slug>/<repo> remote get-url origin
   ```

   Normalize to `https://github.com/<owner>/<repo>`: strip a trailing `.git`, and rewrite
   `git@github.com:<owner>/<repo>.git` → `https://github.com/<owner>/<repo>`.

3. **Build the compare link** (three dots = diff since the branch left `main`, which is what "changed
   vs main" means; it is also the page GitHub offers "Create pull request" from):

   ```
   https://github.com/<owner>/<repo>/compare/main...feature/<slug>
   ```

   URL-encode the branch only if it contains characters GitHub won't take literally (`/` is fine).

4. **Summarize the diff next to each link** so the user knows the size before clicking:

   ```bash
   git -C <workspace-root>/.worktrees/<slug>/<repo> fetch origin main
   git -C <workspace-root>/.worktrees/<slug>/<repo> diff --stat origin/main...feature/<slug>
   git -C <workspace-root>/.worktrees/<slug>/<repo> log --oneline origin/main..feature/<slug>
   ```

Present one line per repo, e.g.:

```markdown
- **monkify-backend** — `feature/<slug>` (3 commits, 12 files, +410/-56)
  → https://github.com/marcusruas/monkify-backend/compare/main...feature/<slug>
- **monkify-front** — `feature/<slug>` (1 commit, 4 files, +88/-12)
  → https://github.com/VitorIsaacSM/monkify-front/compare/main...feature/<slug>
```

If a repo was **not** pushed (nothing changed in it), say so instead of emitting a link — an empty
compare page reads as a failed deploy. Rebuild these links after **every** follow-up commit in Step 9
(the URL is stable, the diff stats are not) and re-send them with the new hand-off.

### Step 10: Deploy the feature to the local Docker stack

Boot this feature's code in the local containers via `setup_local_environment.bat`, so the user tests
the **real running app**, not a working tree. Because that script is **single-tenant and destructive**
— it runs `docker compose down -v` (deletes the postgres/redis volumes) and uses **fixed ports**
(`6001` api, `6002` consumers, `6003` front, `5432`/`6379`/`5672`) and **fixed container names**
(`rabbitmq`, `redis`, `monkify-front`) — running it blindly would **destroy whatever feature is
already deployed and wipe its database**. So **detect first, then decide**.

**Deployed-state marker.** Track what is currently deployed in a small JSON file at the workspace
root: `<workspace-root>/.worktrees/.deployed-stack.json` (scratch, outside every repo). Shape:

```json
{ "kind": "feature", "label": "<slug>", "cards": ["<card title>"],
  "branches": { "monkify-backend": "feature/<slug>", "monkify-front": "feature/<slug>" },
  "worktreeBase": ".worktrees/<slug>", "deployedAt": "YYYY-MM-DD" }
```

For a `Combine` deploy (below), use `"kind": "combined"` and list every card + branch included.

**10a — Detect what's running.** Read the marker (if present) and check the containers:

```bash
docker compose ls                          # any monkify stack up?
docker ps --filter "name=monkify" --format "{{.Names}}"
```

Classify:

- **Nothing up** (no monkify containers; marker absent or stale) → **clean deploy** (10c).
- **This same feature up** (`marker.label == <slug>`) → **redeploy** this feature (10c); `down -v` is
  fine, it's the same feature.
- **A different feature / combo up** → **do not clobber**. Go to 10b and ask.

**10b — A different feature is already deployed → ask (never auto-clobber).** Tell the user exactly
what is running (the marker's cards/branches), then offer three choices:

- **Replace** — tear the running stack down and deploy **only this feature** (10c). ⚠️ Warn first that
  the other feature's environment **and its database will be wiped**; it can be redeployed later from
  its own card with `/monkify-implement-feature`.
- **Combine** — rebuild **one** stack containing the currently-deployed feature branch(es) **plus**
  this feature, in a throwaway combined worktree per repo (so the hubs and the feature worktrees are
  untouched):
  1. Derive a short combined `<label>` (join the card slugs, sanitized to `[a-z0-9+-]`).
  2. For **each** of `monkify-backend` / `monkify-front`, create a fresh worktree off `origin/main`:
     `git -C <workspace-root>/<repo> worktree add ../.worktrees/test-<label>/<repo> -b test/<label> origin/main`
     (never pushed — local throwaway).
  3. In each repo's combined worktree, merge every feature branch that affects it (the currently
     deployed one(s) **and** this feature): `git -C .../.worktrees/test-<label>/<repo> merge --no-edit origin/feature/<each-slug>`.
     **Stop on merge conflict** — surface the repo + conflicting files (it's a real integration
     finding); don't `--force` or auto-resolve. Ask the user how to proceed.
  4. Boot from the **backend** combined worktree's script so `..\monkify-front` resolves to the front
     combined worktree (apply the yarn.lock copy from 10c step 2 first):
     `call <workspace-root>\.worktrees\test-<label>\monkify-backend\scripts\setup_local_environment.bat`.
  5. On success, write the marker with `"kind": "combined"`, all cards, and per-repo branch lists.
  When the user is done with the combined stack, the throwaway `test/<label>` worktrees/branch can be
  removed (`git worktree remove` + `git branch -D test/<label>`) — the hubs are untouched.
- **Skip** — leave the running env untouched; **do not deploy**. Hand off (Step 11) telling the user
  this feature was **not** deployed and how to deploy it themselves later (Replace/Combine, or run the
  script from its worktree).

**Never auto-clobber: if the user is unavailable or doesn't answer, default to Skip** — never destroy
a running environment without an explicit choice. Make clear in the hand-off that the feature is
committed/pushed but **not** deployed, and why.

**10c — Build & boot this feature.** The script lives in `monkify-backend/scripts` and builds the
frontend from `..\monkify-front` **relative to the backend checkout**, so run the **backend
worktree's own copy** so both resolve to the feature worktrees, never the hubs:

1. **Ensure both repo checkouts exist at the worktree base** so `..\monkify-front` resolves:
   - Backend at `.worktrees/<slug>/monkify-backend` — the feature worktree (from Step 3). If the
     feature doesn't touch the backend, create one at `origin/main`
     (`git -C <workspace-root>/monkify-backend worktree add ../.worktrees/<slug>/monkify-backend origin/main`).
   - Front at `.worktrees/<slug>/monkify-front` — the feature worktree if front is affected; else
     create one at `origin/main` the same way. The script always builds a `monkify-front` container.
2. **yarn.lock gotcha** — copy the hub's `monkify-front/yarn.lock` into the front worktree **before
   building**; front builds from a worktree fail silently on the gitignored lockfile and boot a
   **stale** image. After boot, sanity-check the served bundle actually changed.
3. Run the backend worktree's script (long build — let it finish):

   ```bat
   call <workspace-root>\.worktrees\<slug>\monkify-backend\scripts\setup_local_environment.bat
   ```

   It `docker compose down -v`s any existing stack, rebuilds `api`/`consumers`/`database`/`redis`/
   `rabbitmq` from the backend worktree (a **compose** project), materializes the frontend
   `.env.production.local` (dev launch buttons force-enabled), and **then** builds + `docker run`s
   `monkify-front`.

   > **The frontend is deployed OUT-OF-BAND from the backend — verify them together (this is the gap
   > that makes a deploy look half-done).** `monkify-front` is a **standalone `docker build` + `docker
   > run`**, *not* a compose service, and it is (re)built **last**, after the whole backend rebuild.
   > Consequences you must account for: (a) `docker compose down -v` does **not** remove the old
   > `monkify-front` — the **previous** feature's front keeps running for the **minutes** the backend
   > takes to rebuild, pointing at a torn-down/rebuilding backend (a real front-up / backend-down
   > window); (b) the batch script has **no error handling** — `call` continues past a failed
   > `docker compose build`, so a **backend** build failure still proceeds to rebuild the front,
   > leaving a **front-up / backend-missing** stack; (c) seeing the front up — or the script returning
   > — is **not** proof the backend deployed.
4. **Verify the FULL stack came up together (do not trust the script's exit).** After the script
   returns, poll `docker ps` until **all five backend compose services** — `…-api-1`, `…-consumers-1`,
   `…-database-1`, `redis`, `rabbitmq` — **and** the `monkify-front` container are **Up / healthy at the
   same time** (allow the backend a few minutes to pass its healthchecks). If, after that wait, the
   **api/consumers are missing or unhealthy while the front is up** (the common split — backend build
   failed), or the front never rebuilt, the deploy **FAILED**: report exactly which side is missing,
   leave the containers/worktrees for inspection, and **do not** hand off as deployed or move the card.
   Re-run the script (or fix the underlying build error) until backend **and** front are healthy together.
5. On confirmed **joint** health (backend + front), sanity-check the served front bundle actually
   changed (yarn.lock gotcha, step 2), then **write/update the marker** (`.worktrees/.deployed-stack.json`)
   with this feature's label, cards, branches, and date.

### Step 11: Hand off to the user to test the running containers — WAIT for the nod

Tell the user the feature is implemented, green, committed/pushed, and **deployed** — only claim
**deployed** once Step 10c's **joint** health check passed (the backend compose services **and** the
`monkify-front` container all Up/healthy together, not merely because the script returned or the front
is up). Then **ask them to test the running app** before the card moves. Give them:

- **Where it's live** — API `http://localhost:6001`, frontend `http://localhost:6003` (dev launch
  buttons enabled). If they chose **Skip** (10b), say clearly it is **not** deployed and how to deploy
  it themselves.
- **The GitHub compare links from Step 9b** — one per pushed repo, each with its commit/file/line
  counts, so they can review the full diff **against `main`** in the browser. These are mandatory in
  the hand-off, not optional extras.
- What changed (per repo, short bullets) and which acceptance criteria are now satisfied.
- Any manual QA steps from the spec's **Manual QA** section.

**Do not move the card until the user gives an explicit go-ahead.** If they report problems, loop back
to Step 6, fix, re-verify (Step 8), make a **follow-up** commit + push (Step 9), **redeploy** (Step
10), and hand off again.

### Step 12: Trello — move card to Implemented

Using `cardId`:

1. `get_lists` → capture the **`Implemented`** list id. If no list is named exactly `Implemented`,
   ask the user which list to use.
2. `move_card` with `cardId`, the **`Implemented`** list id, and `boardId`.
3. Optionally `add_comment` with a one-line implementation note (branch, tests added, date).

Do **not** rename the card or edit its title/description beyond an optional status note.

### Step 13: Report

Tell the user:

1. **Card** — name + link; moved to **Implemented**; branch/spec/worktree read from the
   **Implementation** section.
2. **Plan** — where the writing-plans plan lives.
3. **Implemented** — per-repo summary of what was built; acceptance criteria covered.
4. **Tests** — **exactly which unit tests** were added/changed (per `constraints.md` §3) and the
   integration test(s) added; confirm both suites are green (or, if `level == Minor change`, note that
   the integration test was skipped under the level-based exemption).
5. **Committed + pushed** — `feature/<slug>` in which repos, **each with its GitHub compare link**
   (`…/compare/main...feature/<slug>`, Step 9b) and its commit/file/line counts, so the user can see
   exactly what changed versus `main`. Name any affected repo that got **no** push instead of
   emitting an empty link.
6. **Deployed** — how the feature was deployed (clean / redeploy / **Replace** / **Combine** / not
   deployed because the user chose **Skip**); the running URLs (API `:6001`, front `:6003`) and, for a
   Combine, which other cards share the stack.
7. **Open items** — anything deferred, any spec gap the user resolved mid-implementation.
8. **Next** — run `/monkify-ship-feature <card name>` to sync the KB and merge to `main`.

## Hard rules

- **Ask the answer mode before your first question to the user** (`AGENTS.md` rule 9 /
  `constraints.md` §7.0): *short* (question + options + recommendation) or *complete* (each option
  with today's behavior and its trade-offs). Ask it once per run — before the first spec-gap question
  or the Step 10b Replace/Combine/Skip choice — and reuse it; unknown/unanswered ⇒ **complete**. If
  the mode was already chosen earlier in the same chat, reuse it rather than re-asking.
- **Push, then hand over the compare links** — after the tests are green, the branch is pushed
  (Step 9) and **every** pushed repo gets its GitHub `compare/main...feature/<slug>` link (Step 9b)
  in both the hand-off (Step 11) and the report (Step 13). Derive the URL from each repo's own
  `origin` remote (backend and front have different owners); verify the pushed SHA matches local
  `HEAD` before linking; rebuild the links after every follow-up commit.
- **Plan before code** — use writing-plans first; implement against the spec, not from memory.
- **Stop on spec/cross-feature conflict** — ask the user; never guess (INDEX.md).
- **Commit + push and deploy before hand-off** — the user tests the committed, containerized build
  (not an uncommitted tree). The **card move to Implemented** still waits for their explicit nod.
- **Never auto-clobber a running stack** — if a different feature is already deployed, ask
  (Replace / Combine / Skip); with no answer, default to **Skip** and never wipe a running env.
- **Frontend deploys out-of-band from the backend — verify them together.** `monkify-front` is a
  standalone `docker build`/`docker run` (not a compose service), rebuilt **last**; `down -v` doesn't
  remove it and the batch script doesn't stop on a backend build failure. Never claim **deployed** until
  `docker ps` shows the backend compose services (`api`, `consumers`, `database`, `redis`, `rabbitmq`)
  **and** `monkify-front` all Up/healthy **together** (Step 10c.4). A front-up / backend-missing stack is
  a **failed** deploy — report it, don't hand off, don't move the card.
- **The card's `Level` drives the integration test** (read from the `## Implementation` block):
  `Feature` → a happy-path integration test is mandatory (and one per complex flow, see
  `constraints.md`); `Minor change` → exempt. The classification comes from refinement, not an ad-hoc
  call here; only override it (with the user) if the code written clearly contradicts the level.
- **Worktree-first** — implement in `.worktrees/<slug>/<repo>`; the hub stays on `main`. Deploy by
  running the **backend worktree's** `setup_local_environment.bat` so `..\monkify-front` resolves to
  the front worktree, never the hubs.
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
