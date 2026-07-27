# SpaceStroker — Constraints (non-negotiable)

> Read this **before** writing code, running commands, or touching git. These rules override
> convenience. When in doubt, stop and ask the user.

## 1. Secrets & configuration — only ever write MOCK values

- You **may** read, create, and edit secrets/config files (e.g. `appsettings.json`,
  `appsettings.*.json`, `secrets.json`, `web.config`, `spacestrokersettings.json`, `.env`, `.env.*`),
  **but only ever put MOCK / placeholder values in them.** Never write a real credential, key,
  token, connection string, or secret into any file.
- Never print, echo, copy, or commit a **real** secret value. If you encounter a real secret while
  editing one of these files, leave it untouched (or replace it with an obvious mock only when the
  task requires it) — do not surface it in chat, logs, or commits.
- Use clearly fake placeholders (e.g. `MOCK_VALUE`, `your-key-here`, `Host=localhost;...;Password=postgres`)
  so it is unmistakable that the value is not production.
- Real provider credentials (Solana owner keys, PayPal, Braintree/Venmo, Stripe, DB, RabbitMQ,
  Redis) are assumed to already exist in the real environment and are bound via the existing
  settings mechanism (`GeneralSettings`). Your job is never to supply the real ones.
- Still never commit files containing real secrets (see §2).

## 2. Git — a worktree per repo, per feature (the branch is the merge/PR ref)

The model is **worktree-first**: every feature's work lives in its own isolated worktree, and the
branch is a derived detail — it still exists and is still pushed to `origin` so you get a normal
GitHub branch and PR, but it is **not** the thing you coordinate on. This is what lets multiple
agents work different features at once without colliding.

- **The shared checkout is a permanent `main` hub — never switch its branch.** Each repo's shared
  clone (`<workspace-root>/<repo>`) stays on `main`. Skills run only git that does **not** mutate the
  hub's working tree against it: `git fetch`, `git worktree add|remove|list|prune`,
  `git branch --list`, `git ls-remote`, and `git pull --ff-only origin main`. **Never**
  `git checkout` / `git switch` to a feature branch, and **never** `git checkout -b`, in the hub.
  (Two agents each running `git checkout -b` in one shared checkout is exactly the collision this
  rule prevents.)
- **All feature work happens in a per-feature worktree — one per affected repo.** Create it from the
  hub, off `origin/main`, at the unified path `<workspace-root>/.worktrees/<slug>/<repo>`:

  ```bash
  # run against the hub; does NOT touch the hub's working tree or HEAD
  git -C <workspace-root>/<repo> fetch origin
  git -C <workspace-root>/<repo> worktree add ../.worktrees/<slug>/<repo> -b feature/<slug> origin/main
  git -C <workspace-root>/.worktrees/<slug>/<repo> push -u origin feature/<slug>
  ```

  The `.worktrees/` dir sits under the workspace root and is untracked by every repo, so it is never
  committed. Prefer a native worktree tool if your harness provides one; otherwise use the commands
  above. Do the repo's work **inside** its worktree, never in the hub.
- **Collision pre-flight (before creating anything).** For each affected repo, against the hub:
  `git fetch origin`, then `git worktree list`, `git branch --list feature/<slug>`, and
  `git ls-remote --heads origin feature/<slug>`. If the worktree or the branch already exists (local
  or origin), or `git status` shows the hub is **not** clean-on-`main`, **stop and report** — pick a
  different slug or the feature is already in flight. Git itself refuses to check out one branch in
  two worktrees, which backstops this.
- **A branch (for the PR) is still created and pushed in EACH affected repository**
  (`monkify-backend`, `monkify-front`, and `monkify-meta` when the spec lives there). Use the
  feature's agreed name, e.g. `feature/operators` — same name in every repo — created via the
  worktree above, and pushed to `origin` right after (see `AGENTS.md` → push after branch work).
- Never commit directly to the default branch. Only commit when the user asks; never push force or
  destructive git operations unless explicitly requested. **Exception:** the `monkify-ship-feature`
  skill may commit and push its **knowledge-base sync** to `main` without review — that commit is
  part of the skill's own workflow (see `AGENTS.md` → Must-follow rule 7). This exception covers only
  the KB sync in that skill; all other `main` commits still need approval.
- Never update git config. Never commit secrets/config files (see §1).

## 3. Unit tests (backend changes)

- When you change **backend** behavior in `monkify-backend`, **update the affected unit tests so
  they pass and assert the intended behavior**, and **add tests** for new behavior. Test project:
  `SpaceStroker.Tests` (xUnit + Shouldly + Moq + Bogus; in-memory EF).
- At the **end of the work, report exactly which unit tests were changed or added.**
- Run the full suite before declaring done.
- **Game-flow changes also update the integration tests.** Any change to the game
  lifecycle/flow must update the separate **`SpaceStroker.Tests.Integration`** (Testcontainers) project,
  run via `scripts/run_integration_tests.bat`. See `conventions/backend.md` → Tests.

### Integration tests are mandatory for every new feature

- **Every new feature ships with a backend integration test for its happy path.** For each **new
  feature**, `SpaceStroker.Tests.Integration` (the Testcontainers suite in `monkify-backend`) **must**
  contain **at least one integration test** that drives the feature's **happy path end to end** and
  **asserts it works 100%** (real API + Consumers hosts against real Postgres/RabbitMQ/Redis
  containers, following the `GameFlowTests` pattern — not a mock-only unit test).
- **Complex flows get their own integration test.** If the feature introduces a **particularly
  complex flow** beyond the happy path (e.g. a multi-step state transition, a refund/rollback path,
  a fail-closed branch), that flow **must** have a dedicated integration test as well.
- **Scope of "new feature".** This mandate applies to **features** only. It does **not** apply to
  **bug fixes**, **QOL / dev-experience tweaks**, or **minor changes** — those still follow the unit
  test discipline above but do not require a new integration test unless they change game flow.
- This is checked at ship time: `monkify-ship-feature` verifies the integration suite is green
  before syncing the KB.

> Note on the team rule "always run unit tests for Central de Reservas": that rule is for **CR**,
> a different app. For **SpaceStroker** the equivalent expectation is the test discipline above
> (update + add + report changed tests).

## 4. Database & migrations

- **EF configuration is inline** in `SpaceStrokerDbContext.OnModelCreating`. Do **not** introduce
  `IEntityTypeConfiguration` classes — keep new mapping inline in the existing style.
- **Migrations are applied only by `SpaceStroker.Consumers`** at startup (`Database.Migrate()`). The API
  does not migrate. Add new migrations under `SpaceStroker.Infrastructure/Migrations/`.
- Never hand-edit `SpaceStrokerDbContextModelSnapshot.cs`; regenerate it with `dotnet ef migrations add`.

## 5. Production data, queries & external APIs

- **Do not run database queries or external API requests** that could touch or expose
  sensitive/production data. These may contain PII and must not pass through the agent. If a query
  or request is needed, **ask the user to run it** and share only what is safe.
- Do not point tools at production endpoints. Local docker-compose is the safe environment.

## 6. User-facing messaging (Operators feature)

- For expired/inactive operators, only ever surface a **vague** "not found / unavailable" message.
  **Never reveal contract status** (e.g. never say "the contract expired"). Reuse the existing
  generic error messages; do not add expiry-specific ones.

## 7. Asking the user to decide — always give the trade-offs

Every time you ask the user a question that decides how something gets built — "should I implement
X?", "which approach do you want?", "how should this behave when Y?", or presenting a plan for
approval — you **must** explain, **in detail**, the trade-offs of the proposal **against the current
functionality**. A bare question, or an option list with no consequences attached, is not acceptable.

### The required format

For **each** option on the table, state **what happens today** in that area (the current behavior it
replaces or bends, why it works that way, and where it lives — `features/*.md`, the spec, or the
code; "nothing exists yet" is a valid answer, but say it), then give the trade-offs in **exactly**
this two-line format:

```
Benefits: X, Y, Z
Issues that this approach brings: A, B, C
```

Both lines are mandatory, on every option. Then give a **recommendation and the reason for it** — do
not leave the user to weigh a neutral menu.

Worked shape:

```markdown
**Option A — <name>**
Today: <current behavior + where it lives>.
Benefits: <gain>, <gain>, <gain>
Issues that this approach brings: <cost>, <risk>, <what it makes harder later>

**Option B — keep the current behavior**
Today: <same current behavior>.
Benefits: <no migration, no new test surface, nothing to undo>
Issues that this approach brings: <the problem stays unsolved, ...>

**Recommendation:** <option> — <why>.
```

### What must appear on those two lines

- **Benefits** — the problem it actually solves and for whom (player, operator, ops), plus anything
  it makes cheaper or simpler later.
- **Issues that this approach brings** — be specific, not generic: added complexity, blast radius
  across other features (game/bet lifecycle, payments & payouts, refunds, operators/multi-tenancy,
  SignalR contracts, caching, scheduling), schema/migration impact, performance, ops burden, new test
  surface (including the mandatory integration test if it is a `Feature`), **how hard it is to undo**
  once shipped (config flip vs. migration + data backfill vs. a public contract we can't take back),
  and anything it makes harder later.

### Rules of thumb

- **Keeping the current behavior is always one of the options**, with the same two lines. State it,
  never imply it.
- Never drop the `Issues` line. If an option genuinely has no real downside, still write it and say
  so — `Issues that this approach brings: none beyond one extra query`. An option with no stated cost
  reads as an omission.
- Scale the depth to the stake: a naming choice gets a couple of short items per line; anything
  touching money, migrations, or a shipped feature's behavior gets the full treatment.
- Ground it in the codebase, not in generalities — cite the current behavior from `features/`,
  `specs/`, or the live code, and be honest when you are unsure.

## 8. Scope & safety defaults

- Prefer editing existing files over creating new ones; don't create docs/files unless asked.
- For reversible, low-risk choices (naming, formatting, equivalent approaches), decide and note it.
  For scope changes or destructive actions, ask first.
- Keep the two deployable hosts' responsibilities intact: the **API** stays stateless and does not
  migrate; background/money/typer work stays in **`SpaceStroker.Consumers`**.
