---
name: monkify-test-features
description: Spin up a local Monkify environment that combines the work of several in-flight Trello cards at once, so the user can test multiple features together. Use when the user names two or more monkaify cards and wants to try their branches together locally. Creates a throwaway test worktree per repo off main, merges each card's feature branch into a temp test branch, runs setup_local_environment.bat to build/boot the combined stack, and after testing tears the git side down (removes the worktrees and deletes the temp branch), leaving the containers running. Needs Trello MCP, Docker, and the sibling repos.
disable-model-invocation: true
---

# Monkify — Test Features (multi-card local test)

Stand up **one local environment** that contains the work of **several** Trello cards at once, so the
user can test the features **together** before they ship. The combined code is built in throwaway
**test worktrees** off `main`; when the user is done, the git side is torn down and the hub is exactly
as it was before.

This skill **does not** implement, spec, or ship anything, and it **does not move any Trello card** —
it is a disposable integration-test harness. Implementation is `monkify-implement-feature`; shipping
is `monkify-ship-feature`.

Board: `https://trello.com/b/Yi42y6Xy/monkaify` (short id `Yi42y6Xy`).

## Input (required)

The user names **two or more** Trello cards (exact titles or unambiguous substrings), e.g.:

> `/monkify-test-features Add Free bet mode, Player customization modal, Session chat`

Each card's **branch** and **repos** are read from its **`## Implementation`** section (written by
`/monkify-create-spec`; see `../monkify-create-spec/references/spec-card-metadata.md`). If a named
card has no **Implementation** block / **Branch** line, **stop** and tell the user that card isn't
spec'd yet (run `/monkify-create-spec` for it, or drop it from the list).

## Prerequisites (verify FIRST — stop if missing)

1. **Trello MCP** — server `user-trello`. Verify with `list_boards`. If missing, **stop** and tell
   the user to install/enable the `trello` MCP server.
2. **Sibling repos** — `monkify-backend` and `monkify-front` exist next to `monkify-meta`.
3. **Clean `main` hubs** — `monkify-backend` and `monkify-front` are **clean and on `main`** (you
   build in test worktrees; the hubs are never switched). If dirty / not on `main`, **stop** and ask
   the user to restore them.
4. **Docker running** — `setup_local_environment.bat` runs `docker compose` and builds images.
   Verify `docker info` succeeds; if not, **stop** and ask the user to start Docker.

## Workflow

Copy this checklist and track progress:

```
- [ ] Step 0: Verify prerequisites
- [ ] Step 1: Resolve board, find every named card, parse each Implementation block
- [ ] Step 2: Derive the combined test label, branch, and worktree paths
- [ ] Step 3: Collision pre-flight (hubs clean-on-main; test paths/branch free)
- [ ] Step 4: Create the test worktree(s) off origin/main (backend + front)
- [ ] Step 5: Merge each card's feature branch into the test branch (per repo)
- [ ] Step 6: Boot the combined stack — run setup_local_environment.bat from the backend worktree
- [ ] Step 7: Hand off to the user to test; WAIT until they're done
- [ ] Step 8: Teardown — remove the worktree(s) and delete the temp branch (git only; leave Docker up)
- [ ] Step 9: Report
```

### Step 1: Resolve board, find every card, parse Implementation

1. `list_boards` → find `monkaify` (`Yi42y6Xy`); `set_active_board`.
2. For **each** named card: find it across lists (`get_cards_by_list_id`); exact name first, ask if
   ambiguous. Capture `cardId`, `cardUrl`, and title.
3. `get_card` (`includeMarkdown: true`) for each; **parse `## Implementation`**:
   - **`branch`** — `feature/<card-slug>`
   - **`repos`** — comma-separated (default `monkify-backend, monkify-front, monkify-meta`)
4. **Verify each branch exists on origin** so the merge can find it:

   ```bash
   git -C <workspace-root>/monkify-backend fetch origin
   git -C <workspace-root>/monkify-front   fetch origin
   git -C <workspace-root>/<repo> ls-remote --heads origin feature/<card-slug>
   ```

   If a card's branch is missing on origin in a repo it claims to affect, **stop** and report (its
   work may not be pushed yet).

Keep, per card: title, `feature/<card-slug>`, and which of `monkify-backend` / `monkify-front` it
affects. (`monkify-meta` is spec/KB only and is **not** built or booted — ignore it here.)

### Step 2: Derive the combined test label, branch, and worktree paths

- **Label** — a short kebab slug identifying this combination. Default: join the card slugs with `+`,
  truncating if long (e.g. `free-bet-mode+session-chat`). If that's unwieldy, ask the user for a
  short label. Sanitize to `[a-z0-9+-]`.
- **Test branch** — `test/<label>` (local, throwaway — **never pushed to origin**).
- **Worktree base** — `<workspace-root>/.worktrees/test-<label>/` with a checkout per repo at
  `.worktrees/test-<label>/monkify-backend` and `.worktrees/test-<label>/monkify-front`.

> **Both** repos always get a test worktree, even if every card is backend-only:
> `setup_local_environment.bat` lives in `monkify-backend/scripts` and builds the frontend from
> `..\monkify-front` **relative to the backend checkout**. Running the script from the backend test
> worktree makes `..\monkify-front` resolve to the sibling **front test worktree**, so the combined
> stack is built from the test worktrees, never from the hubs. A repo with no card branches to merge
> just stays at `origin/main`.

### Step 3: Collision pre-flight

For **each** of `monkify-backend` and `monkify-front`, against the hub:

```bash
git -C <workspace-root>/<repo> status --short --branch                 # clean + on main
git -C <workspace-root>/<repo> worktree list                           # is .worktrees/test-<label>/<repo> taken?
git -C <workspace-root>/<repo> branch --list "test/<label>"            # local branch exists?
```

**Stop and report** (create nothing) if a hub is dirty / not on `main`, or the test worktree path or
`test/<label>` branch already exists. Pick a different label or clean up the previous run first.

### Step 4: Create the test worktree(s) off origin/main

For **each** repo, create a fresh worktree on a new `test/<label>` branch pointed at `origin/main`
(this never touches the hub's HEAD or working tree):

```bash
git -C <workspace-root>/<repo> fetch origin
git -C <workspace-root>/<repo> worktree add ../.worktrees/test-<label>/<repo> -b test/<label> origin/main
```

Do **not** push `test/<label>` — it is a local throwaway.

### Step 5: Merge each card's feature branch into the test branch

For **each** repo, merge every card branch that affects that repo into `test/<label>`, working
**inside that repo's test worktree**:

```bash
# inside .worktrees/test-<label>/<repo>
git -C <workspace-root>/.worktrees/test-<label>/<repo> merge --no-edit origin/feature/<card-a-slug>
git -C <workspace-root>/.worktrees/test-<label>/<repo> merge --no-edit origin/feature/<card-b-slug>
# ...one merge per card that affects this repo
```

**On merge conflict:** stop immediately. Report the repo, the two cards involved, and the conflicting
files — this is a real integration conflict the user should know about **before** shipping either
feature. Do **not** `--force` or auto-resolve. Ask the user how to proceed (drop a card, or resolve
the conflict manually in the worktree). Abort the in-progress merge (`git merge --abort`) if the user
wants to back out.

After a clean merge set, summarize per repo which card branches were combined.

### Step 6: Boot the combined stack

Run the **backend test worktree's own copy** of the setup script (so its relative `..\monkify-front`
resolves to the front test worktree, not the hub):

```bat
call <workspace-root>\.worktrees\test-<label>\monkify-backend\scripts\setup_local_environment.bat
```

This tears down any existing local env (`docker compose down -v`), rebuilds every backend container
(`api`, `consumers`, `database`, `redis`, `rabbitmq`) from the test worktree, materializes the
frontend `.env.production.local` from `monkifysettings.json` + compose ports (dev launch buttons
force-enabled), and builds/boots the `monkify-front` container from the front test worktree. It's a
long build — let it finish. If it fails, report the failing step and stop (leave the worktrees so the
user can inspect).

When it's up, tell the user where things are (per the script): API at `http://localhost:6001`,
frontend at `http://localhost:6003`.

### Step 7: Hand off to the user to test — WAIT

Tell the user the combined environment is live, list which cards/features are included, and ask them
to test everything they need. **Do nothing further until the user says they're done** (or asks to
tear down). While waiting, if they hit issues, help them debug in the test worktree — but this skill
does not commit or ship anything.

### Step 8: Teardown — git only (leave Docker running)

Once the user is done, remove the throwaway git artifacts so nothing stale is left behind. **Leave
the running containers up** (the user may keep poking at them). From each hub
(`<workspace-root>/<repo>`, on `main` — never from inside a worktree you're removing):

```bash
# remove the test worktrees
git -C <workspace-root>/monkify-backend worktree remove ../.worktrees/test-<label>/monkify-backend
git -C <workspace-root>/monkify-front   worktree remove ../.worktrees/test-<label>/monkify-front
git -C <workspace-root>/monkify-backend worktree prune
git -C <workspace-root>/monkify-front   worktree prune

# delete the throwaway integration branch (local only; never pushed)
git -C <workspace-root>/monkify-backend branch -D test/<label>
git -C <workspace-root>/monkify-front   branch -D test/<label>
```

**Notes / safety:**

- `branch -D` (force) is correct **here only** — `test/<label>` is a throwaway integration branch
  that is intentionally never merged to `main`, so the merge-checked `-d` would refuse it. This is the
  one skill where force-delete is expected; it applies **only** to the `test/<label>` branch.
- `git worktree remove` refuses if the worktree has **uncommitted** changes. If the user made edits
  there they want to keep, **stop** and ask before removing (don't `--force`).
- The hubs were never switched or committed to, so removing the worktrees and the local branch
  restores each repo to **exactly** its pre-test state. Confirm with `git worktree list` and
  `git status` on each hub.
- Docker is intentionally left running. If the user later wants a clean machine, they can run
  `docker compose down -v` from `monkify-backend` and `docker rm -f monkify-front`.

### Step 9: Report

Tell the user:

1. **Cards tested** — titles + links, and which feature branch each contributed, per repo.
2. **Environment** — combined stack built from test worktrees and booted (API `:6001`, front `:6003`);
   any merge conflicts surfaced during Step 5.
3. **Teardown** — test worktrees removed and `test/<label>` deleted in both repos; hubs verified back
   on clean `main` (pre-test state). Containers left **running**.
4. **Next** — the user can continue their work; ship each feature with `/monkify-ship-feature <card>`
   when ready. No Trello card was moved by this skill.

## Hard rules

- **No card moves, no commits, no ship** — this is a disposable test harness only.
- **Worktree-first** — build/boot from `.worktrees/test-<label>/<repo>`; the hubs stay on `main` and
  are never switched or committed to.
- **Never push** `test/<label>` — it's local and throwaway.
- **Stop on merge conflict** — surface it; never `--force`-resolve. A conflict between two cards is a
  finding, not a nuisance.
- **`branch -D` is allowed only for the `test/<label>` throwaway branch** — everywhere else in the
  Monkify skills, use merge-checked `-d`.
- Honor `constraints.md` — mock secrets only, never point tools at production, respect worktree-first.

## Trello tools used (server `trello` / `user-trello`)

`list_boards`, `set_active_board`, `get_lists`, `get_cards_by_list_id`, `get_card`.

## References

- `../monkify-create-spec/references/spec-card-metadata.md` — the **Implementation** block each card
  carries (source of each card's branch + repos).
