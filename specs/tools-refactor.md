# SpaceStroker — "Tools refactor" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [#167 Tools refactor](https://trello.com/c/Co35YRGt/167-tools-refactor) (2026-08-11).
> Repos affected: **`monkify-meta` only** — `monkify-backend` and `monkify-front` are **not**
> touched (no entity, handler, consumer, EF mapping, migration, endpoint, SignalR contract,
> composable or component).
>
> **Purpose:** two pieces of housekeeping in `monkify-meta`. (1) Make the `tools/` console apps
> consistent — one shared `secrets.json`, one git-ignored `Receipts/` folder, `SpaceStroker.DbConsole`
> moved in from `deploy/`, the dead `SpaceStroker.Seeder` remnants removed, and `SpaceStroker.Deploy`
> left alone in `deploy/` — and write the resulting layout down as a convention. (2) Shrink the
> knowledge base so agents stop paying for ~5.5 MB of docs, a large share of which describe
> capabilities that no longer exist.
>
> **Level: `Minor change`** — tooling reorganisation + docs. No backend/frontend surface ⇒ **no**
> mandatory happy-path integration test, and **no** new `features/<slug>.md` doc at ship time.

---

## 1. Summary

Seven work items, two of which carry real risk (the shared secrets file holds mainnet private keys;
the KB sweep deletes files irreversibly from the tree).

| # | Item | Risk |
| --- | --- | --- |
| A | One shared `tools/secrets.json` for every project in `SpaceStroker.Tools.sln` | ⚠️ mainnet keys — operator hand-merge |
| B | Move `SpaceStroker.DbConsole` from `deploy/tools/` into the `tools/` solution | low |
| C | Delete the `SpaceStroker.Seeder` remnants | low |
| D | Leave `SpaceStroker.Deploy` alone in `deploy/` (flatten `deploy/tools/`) | low |
| E | New git-ignored `tools/Receipts/` for all run state and reports | ⚠️ funder resume state |
| F | Write A–E into `conventions/backend.md` + `AGENTS.md` | none |
| G | Reduce + consolidate the knowledge base | ⚠️ irreversible in-tree deletion — **gated** |

### Today, in one table

| Thing | Today | After |
| --- | --- | --- |
| Funder secrets | `tools/secrets.json` | `tools/secrets.json` (unchanged sections) |
| Sniper secrets | `tools/secrets.json` | `tools/secrets.json` (unchanged sections) |
| Nightfall secrets | **`tools/secrets.nightfall.json`** (own file) | `tools/secrets.json` → new **`nightfall`** section |
| DbConsole secrets | `deploy/secrets.<env>.json` | `deploy/secrets.<env>.json` (**documented exception**) |
| Funder receipts | `tools/receipts.json` | `tools/Receipts/receipts.json` |
| Sniper receipts | `tools/snipe-receipts.json` | `tools/Receipts/snipe-receipts.json` |
| Sniper reports | `tools/reports/<ts>/` | `tools/Receipts/reports/<ts>/` |
| Nightfall receipts | `tools/nightfall-receipts.json` | `tools/Receipts/nightfall-receipts.json` |
| DbConsole project | `deploy/tools/SpaceStroker.DbConsole/` | `tools/SpaceStroker.DbConsole/` |
| Deploy project | `deploy/tools/SpaceStroker.Deploy/` | `deploy/SpaceStroker.Deploy/` |
| Deploy solution | `deploy/tools/SpaceStroker.DeployTools.sln` | `deploy/SpaceStroker.Deploy.sln` |

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-first (§2):** all work in `.worktrees/tools-refactor/monkify-meta`. The `monkify-meta`
  hub stays on `main` — never `git checkout -b` / `git switch` there. Branch `feature/tools-refactor`
  is already created and pushed. Push after every commit.
- **Secrets — mock only in committed files (§1):** `tools/secrets.example.json` carries `REPLACE_ME`
  placeholders only. The real `tools/secrets.json`, `tools/Receipts/*` and `deploy/secrets.<env>.json`
  stay untracked and must never be printed, echoed, logged or committed. **This work reads and
  restructures a file holding live mainnet private keys and live Coinbase withdrawal credentials —
  never open, cat, or echo the operator's real `secrets.json`.** Work only against
  `secrets.example.json` and the C# binding types.
- **Unit tests (§3):** the `tools/` solution's existing tests must stay green; add tests for the new
  secrets shape, the `Receipts/` defaults and the legacy-receipts detection. `SpaceStroker.Tests`
  (backend) is **not** run — the backend is untouched.
- **Integration test:** **not required.** `Minor change`, and there is no backend code path to
  exercise (same reasoning as `specs/coinbase-wallet-funding.md` and `specs/nightfall-dashboard.md`,
  where the mandate was explicitly waived).
- **EF / migrations (§4):** N/A — no database change.
- **Scope & safety (§8):** prefer editing existing files. Item G deletes files; it is gated on
  explicit user approval of the inventory (§9.3) before a single `git rm`.
- Feature-specific: **`sed -i` is banned for bulk edits in this repo** on Windows — it rewrites CRLF
  repo-wide and produces hundreds of phantom-modified files. Use targeted edits.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **Level = `Minor change`.** Meta-repo tooling + docs, zero backend/frontend surface ⇒ no integration test, no new `features/<slug>.md`. |
| 2 | **Dead KB docs are deleted outright**, not moved to an archive folder. Git history is the archive; an archive folder still gets globbed and re-read by agents, so it saves far fewer tokens than deleting. |
| 3 | **`INDEX.md` keeps its current format and prose style.** Only map rows whose docs were deleted or merged are removed or repointed. The **"Feature currently in flight"** section is left **byte-identical** — knowingly accepted, even though it is ~210 KB of already-shipped history and the largest single block of tokens in the repo. |
| 4 | **KB scope = `INDEX.md` + `features/` + `specs/`.** `docs/superpowers/` plan files are out of scope. |
| 5 | **One shared `tools/secrets.json`**, migrated by the operator **by hand, once**. No auto-migration code touches a file holding mainnet keys. `secrets.nightfall.example.json` is deleted and folded into `secrets.example.json`. |
| 6 | **Nightfall gets its own dedicated `nightfall` section** inside the shared file (not flattened to the top level), so its keys stay visibly grouped. `solana` and `wallets[]` are shared at the top level. |
| 7 | ⚠️ **Decision 6 is an explicit, reasoned reversal of `specs/nightfall-dashboard.md` decision #4**, which kept Nightfall's secrets in their own file because `tools/secrets.json` holds live Coinbase withdrawal credentials — "a different blast radius". **That rationale no longer holds:** `tools/secrets.json` already carries wallet `PrivateKey` values (the sniper binds them, and `secrets.example.json` ships a `PrivateKey` placeholder in `wallets[]`), so withdrawal credentials and spend keys are **already** co-located. Merging Nightfall in creates **no new blast radius**. The code comment in `SpaceStroker.Nightfall/Infrastructure/SecretsFile.cs` that states the old rationale must be replaced, not left contradicting the code. |
| 8 | **`SpaceStroker.DbConsole` keeps reading `deploy/secrets.<env>.json`** — a **documented exception** to "all tools share one secrets.json". Those connection strings are per-environment and synced by the deploy tool; copying production Cloud SQL credentials into a second untracked file would create drift and a second place to leak from. |
| 9 | **Receipts go to `tools/Receipts/`**; the loose files already in `tools/` are **left where they are** and moved by the operator if still wanted. Auto-relocating them would disturb the funder's plan-hash/idempotency double-fund protection. |
| 10 | **`deploy/tools/` is flattened** — `SpaceStroker.Deploy` and its test project move up to `deploy/`, so the deploy app is literally "alone inside the deploy folder". The solution is renamed `SpaceStroker.Deploy.sln`. |
| 11 | **Conventions are written to both** `conventions/backend.md` (a new section) **and** `AGENTS.md` (Quick context). |
| 12 | **Item G is gated.** The implementer produces a full classification inventory of every file in `features/` + `specs/` and **stops for user approval** before deleting or merging anything. Deleting ~100 docs on a criteria description alone is not acceptable. |

---

## 4. Data model

**N/A** — no entity, no EF mapping, no migration. No database is touched by any item in this card.

---

## 5. Configuration

### 5.1 The unified `tools/secrets.json`

One file, read by three of the four projects in `SpaceStroker.Tools.sln`. Shared keys sit at the top
level; each tool keeps a named section. `wallets[]` is the single shared pool and every tool
continues to **ignore unknown fields** in it, which is what lets one array serve all three.

```jsonc
{
  // SHARED — every tool reads this one.
  "solana": { "rpcUrl": "https://mainnet.helius-rpc.com/?api-key=REPLACE_ME_RPC_KEY" },

  // spacestroker-fund-wallets
  "coinbase":   { "apiKeyId": "...", "apiKeySecret": "...", "keyAlgorithm": "ed25519", "accountId": "", "apiHost": "api.coinbase.com", "apiVersion": "2024-01-01" },
  "travelRule": { "beneficiaryWalletType": "WALLET_TYPE_SELF_HOSTED", "isSelf": "IS_SELF_TRUE", "...": "" },
  "funding":    { "spreadMinutes": 45, "networkFeeHeadroomSol": 0.05, "minAmountSol": 0.001, "sendPollTimeoutSeconds": 300, "chainConfirmTimeoutSeconds": 120 },

  // spacestroker-snipe-token
  "snipe":      { "mint": "REPLACE_ME_TOKEN_MINT_ADDRESS", "spreadMinutes": 60, "slippageTolerance": 0.25, "...": null },

  // spacestroker-nightfall — NEW dedicated section (decision 6)
  "nightfall": {
    "token":   { "mint": "REPLACE_ME_TOKEN_MINT_ADDRESS" },
    "jupiter": { "apiBaseUrl": "https://lite-api.jup.ag/swap/v1", "slippageBps": 300, "maxPriceImpactPercent": 10, "prioritizationFeeLamports": 0, "quoteCacheSeconds": 10 },
    "sell":    { "maxConcurrentSells": 4, "minSolForFeeSol": 0.003, "refreshIntervalSeconds": 15, "confirmTimeoutSeconds": 90, "closeEmptiedTokenAccounts": true }
  },

  // SHARED pool — unknown fields ignored by every tool.
  "wallets": [
    { "alias": "W01", "Label": "W01", "PublicKey": "REPLACE_ME_BASE58_ADDRESS", "PrivateKey": "REPLACE_ME_BASE58_SECRET_KEY", "SOLAmount": 2.54, "SupplyPercentage": 0.4504 }
  ]
}
```

⚠️ **`snipe.mint` and `nightfall.token.mint` stay separate keys on purpose.** They are the same CA in
practice, but the sniper refuses a graduated curve while Nightfall requires a tradable route — folding
them into one key would let a single edit silently retarget both the buy and the sell side.

**Per-tool `rpcUrl` defaults differ today and must stay as they are:** the funder defaults to
`https://api.mainnet-beta.solana.com` (it only reads balances), while the sniper and Nightfall default
to `""` and **fail closed** because they broadcast. Do not unify the defaults.

### 5.2 What is deleted

| File | Action |
| --- | --- |
| `tools/secrets.nightfall.example.json` | **Delete** — its three sections move under `nightfall` in `secrets.example.json`. |
| `deploy/secrets.seeder.json` | **Delete** — the tool it configured is gone. |

### 5.3 `tools/.gitignore` — after

The current file carries three separate hacks (`*receipts*.json`, `/reports/`, `/snipe-receipts*.json`)
because run state landed at four loose paths. One folder collapses them:

```gitignore
# Local secrets — the operator's source of truth. NEVER commit (only the template is tracked).
secrets*.json
!secrets.example.json

# Run state, receipts and reports — addresses, amounts, transaction ids and signatures.
/Receipts/

# .NET build output for the console apps
**/bin/
**/obj/
```

⚠️ Keep the **legacy** ignore lines for one more cycle **only if** the operator still has loose
receipts files in `tools/`; otherwise a `git status` after this change would offer to commit
`tools/receipts.json` (which contains real addresses and transaction ids). **Safer: keep
`*receipts*.json` and `/reports/` in the file** alongside `/Receipts/`, with a comment saying they
cover pre-refactor leftovers and can be dropped once `tools/` is clean. Verify with
`git check-ignore -v tools/Receipts/receipts.json tools/receipts.json`.

---

## 6. Tooling behaviour (`monkify-meta/tools`, `monkify-meta/deploy`)

### 6.1 Item A — the shared secrets file

**`SpaceStroker.WalletFunder`** and **`SpaceStroker.TokenSniper`** already default to
`tools/secrets.json`. **No change** beyond the example template.

**`SpaceStroker.Nightfall`** changes:

1. `Application/CliOptions.cs` — `o.SecretsPath = Path.Combine(toolsRoot, "secrets.json")`
   (was `secrets.nightfall.json`). Update `PrintUsage()`'s `--secrets` default line.
2. `Infrastructure/SecretsFile.cs` — introduce a `NightfallSection`:

   ```csharp
   public sealed class SecretsFile
   {
       [JsonPropertyName("solana")]    public SolanaSection Solana { get; set; } = new();     // shared
       [JsonPropertyName("nightfall")] public NightfallSection Nightfall { get; set; } = new(); // NEW
       [JsonPropertyName("wallets")]   public List<RawPoolEntry> Wallets { get; set; } = [];  // shared
       ...
   }

   public sealed class NightfallSection
   {
       [JsonPropertyName("token")]   public TokenSection Token { get; set; } = new();
       [JsonPropertyName("jupiter")] public JupiterSection Jupiter { get; set; } = new();
       [JsonPropertyName("sell")]    public SellSection Sell { get; set; } = new();
   }
   ```

3. **Every validation message must name the new path.** `token.mint` → **`nightfall.token.mint`**,
   `jupiter.slippageBps` → **`nightfall.jupiter.slippageBps`**, `jupiter.maxPriceImpactPercent`,
   `sell.maxConcurrentSells`. A message naming a key that no longer exists is worse than no message.
4. The not-found message changes from
   *"Copy tools/secrets.nightfall.example.json"* → *"Copy tools/secrets.example.json"*.
5. **New fail-fast:** if the file loads but the `nightfall` section is **absent** (an operator who
   has not hand-merged yet), throw a `NightfallException` that says exactly that and points at
   `secrets.example.json`. Without this the operator gets `nightfall.token.mint is still a
   placeholder`, which sends them looking for the wrong problem.
6. **Replace the class doc comment.** The current one asserts the file is "deliberately its OWN file"
   — it would be actively misleading. Replace it with decision 7's reasoning: the file is shared, and
   it already held spend keys before this change.
7. All downstream reads move from `secrets.Token` / `secrets.Jupiter` / `secrets.Sell` to
   `secrets.Nightfall.Token` / `.Jupiter` / `.Sell` — `Program.cs`, `Application/PollLoop.cs`,
   `Application/SellRunner.cs`, `Application/ApiEndpoints.cs`, `Application/ConsoleReport.cs`,
   `Application/NightfallState.cs`, `Infrastructure/Jupiter/*`, `Infrastructure/Solana/*`.
   Compile-driven; do **not** `sed` the repo.

**Not changed:** `RawPoolEntry` / `WalletPool` / keypair validation / the duplicate-address check —
the pool contract is already shared and already ignores unknown fields.

### 6.2 Item B — move `SpaceStroker.DbConsole`

- `git mv deploy/tools/SpaceStroker.DbConsole tools/SpaceStroker.DbConsole`.
- Add the project to `tools/SpaceStroker.Tools.sln`; remove it from the deploy solution (§6.4).
- **No code change.** `FindSecretsFile` walks up from **`AppContext.BaseDirectory`** looking for
  `<dir>/monkify-meta/deploy/secrets.<env>.json`. From
  `tools/SpaceStroker.DbConsole/bin/<cfg>/net8.0` that walk reaches the workspace root
  (`E:/Monkify`) exactly as it does from `deploy/tools/...`, so the lookup **still resolves after the
  move** — verified by reading the walk, and asserted by a new test (§11).
- ⚠️ **Windows gotcha:** a stray `dotnet` process locks worktree *directories*, so `git mv <dir>`
  fails with Permission denied. Kill any running `dotnet` first, or move the children and then the
  now-empty parent.

### 6.3 Item C — delete the `SpaceStroker.Seeder` remnants

The **source is already gone** and the project is in **no** solution. What remains:

| Path | Action |
| --- | --- |
| `deploy/tools/SpaceStroker.Seeder/{bin,obj}` | delete |
| `deploy/tools/SpaceStroker.Seeder.Tests/{bin,obj}` | delete |
| `deploy/secrets.seeder.json` | delete (untracked — delete from disk, nothing to commit) |

These are build artifacts, so they are already git-ignored — this is a **working-tree cleanup**, not a
commit. Confirm with `git status` that nothing tracked disappears.

⚠️ **Do not touch** `architecture.md` §8e's sentence *"Replaces `SpaceStroker.Seeder`, the manual
6-hourly console app, which is deleted"* — it is accurate historical context explaining why the house
seed bet exists. Likewise leave `specs/auto-bet-seeder.md`, `specs/house-seed-bet.md` and
`features/house-seed-bet.md` alone: their "seeder" is the **backend auto-bet seeder**, a different
thing that shares the word. Item G may still merge those docs on their own merits.

### 6.4 Item D — `SpaceStroker.Deploy` alone in `deploy/`

```
deploy/
  SpaceStroker.Deploy/            # moved up from tools/
  SpaceStroker.Deploy.Tests/      # moved up from tools/
  SpaceStroker.Deploy.sln         # renamed from tools/SpaceStroker.DeployTools.sln
  secrets.example.json
  terraform/{persistent,workload}
  docs/
```

- `git mv` both projects up; delete the now-empty `deploy/tools/`.
- Rewrite the solution: drop the `SpaceStroker.DbConsole` entry, repoint the two remaining project
  paths, rename the file. Keep the existing project GUIDs so nothing else has to change.
- `RepoPaths.cs` needs **no change** — it composes `MetaRepo/deploy/terraform/...`, which is
  unaffected by where the project file sits. Verify `MetaRepo` resolution still holds if it is
  derived from `AppContext.BaseDirectory` depth.
- `deploy/.gitignore` needs no change (`**/bin/`, `**/obj/` still match).

### 6.5 Item E — `tools/Receipts/`

| Project | `CliOptions` default today | Default after |
| --- | --- | --- |
| WalletFunder | `toolsRoot/receipts.json` | `toolsRoot/Receipts/receipts.json` |
| TokenSniper | `toolsRoot/snipe-receipts.json` | `toolsRoot/Receipts/snipe-receipts.json` |
| TokenSniper (reports) | `toolsRoot/reports` | `toolsRoot/Receipts/reports` |
| Nightfall | `toolsRoot/nightfall-receipts.json` | `toolsRoot/Receipts/nightfall-receipts.json` |

- Each tool creates `Receipts/` on demand (`Directory.CreateDirectory` on the parent before the first
  write). The sniper already does this for `reports/<timestamp>/`; the funder and Nightfall write a
  file directly and must not fail on a missing folder.
- `--receipts` / `--reports` still override, unchanged. Update all three `PrintUsage()` bodies.

**Legacy-file detection (the double-fund guard).** `spacestroker-fund-wallets` derives each wallet's
Coinbase idempotency token from the plan hash; a receipts file it cannot see reads as "no prior run".
Moving the default path must therefore never look like a fresh start:

> On startup, if the resolved receipts path does **not** exist **and** a legacy file exists at
> `toolsRoot/receipts.json`, **stop** with:
> *"Found a pre-refactor receipts file at `tools/receipts.json` but nothing at
> `tools/Receipts/receipts.json`. Move it there (or pass `--receipts`) before running — starting fresh
> could re-send a transfer."*

Apply the same check, as a **non-fatal warning**, to the sniper (`snipe-receipts.json`) and Nightfall
(`nightfall-receipts.json`): both resume from on-chain state rather than from receipts, so a missing
file cannot double-spend there — but a silently ignored audit trail is still worth naming.

### 6.6 Item F — write it down

**`conventions/backend.md`** — append a new top-level section, **"Meta-repo console tools"**:

- `monkify-meta/tools/` = `SpaceStroker.Tools.sln`: WalletFunder, TokenSniper, Nightfall, DbConsole.
  Standalone operator tools. **Nothing here is built into a container image, deployed to Cloud Run,
  or run by the local Docker stack.** Three of the four move real money.
- `monkify-meta/deploy/` = `SpaceStroker.Deploy.sln`: the deploy/teardown app and its tests, next to
  the Terraform stack.
- **One shared, git-ignored `tools/secrets.json`** for the `tools/` solution; shared keys
  (`solana.rpcUrl`, `wallets[]`) at the top level, one named section per tool. The only tracked
  config is the placeholder-only `secrets.example.json`. **Documented exception:** `DbConsole` reads
  `deploy/secrets.<env>.json`, because Cloud SQL connection strings are environment-scoped and owned
  by the deploy tool (decision 8).
- **All run state, receipts and reports go to git-ignored `tools/Receipts/`.** Never write run state
  to the solution root.
- The existing **Domain / Infrastructure / Application** rule ("The folder tree must show the
  architecture") already binds these projects — cross-reference it rather than restating it.

**`AGENTS.md`** — extend **Quick context** with a `monkify-meta` bullet naming `tools/` vs `deploy/`
and the one-secrets-file / `Receipts/` rules, linking to the new `conventions/backend.md` section.
Keep it to a few lines; `AGENTS.md` is loaded into every session.

---

## 7. Frontend behaviour

**N/A** — `monkify-front` is not touched.

---

## 8. Backend behaviour

**N/A** — `monkify-backend` is not touched. No handler, consumer, endpoint, event or test in that
repo changes.

---

## 9. Item G — knowledge-base reduction

The largest and the only irreversible item. It runs in **four phases with a hard stop after phase 2.**

### 9.1 The problem, measured

| Artifact | Size today |
| --- | --- |
| `INDEX.md` | **522 KB / 2,605 lines** — a 188-row map table (~301 KB) + a "Feature currently in flight" section (~210 KB, ~20 already-shipped features) |
| `specs/` | **4.6 MB across 117 files** |
| `features/` | **824 KB across 65 files** (61 of which have an identically-named `specs/` sibling) |
| `docs/superpowers/` | 820 KB — **out of scope** (decision 4) |

### 9.2 Phase 1 — build the inventory (no deletion)

Produce `docs/kb-reduction-inventory.md` (a **temporary review artifact**, deleted before the final
commit) with one row per file in `features/` and `specs/`:

| File | Verdict | Domain | Reason |
| --- | --- | --- | --- |
| `features/x.md` | `KEEP` / `MERGE` / `DELETE` | target domain doc | one line |

**Verdict criteria — apply in this order:**

1. **`DELETE` — the capability no longer exists.** The doc describes behaviour that was removed, and
   nothing in it explains today's system. The operator/multi-tenant stack is the headline case
   (`specs/operator-feature.md`, `operator-admin-page.md`, `operator-player-identity.md`,
   `operator-wallet-api.md`, `launch-token-validation.md`, `rg-pre-bet-hook.md`,
   `main-production-operator.md`, `remove-operators-backfill.md`).
   ⚠️ **`features/remove-operators.md` is `MERGE`, not `DELETE`** — it is the only doc that explains
   *why* there are no operators and what replaced them (`GeneralSettings:Operator`, wallet identity,
   the single ledger). Losing it loses the current architecture, not a dead one.
2. **`DELETE` — a one-off bug fix or QOL tweak that never warranted a standing doc.**
   `features/dev-qol-stuff.md` is the card's named example. Same test for the many small `specs/`
   entries (`bet-choice-casing`, `fair-modal-off-screen`, `near-miss-dock-bg`, `home-hero-trim`,
   `robots-txt`, `idle-websocket-disconnect`, …): if the behaviour it describes is now just how the
   code reads, the doc is noise.
3. **`DELETE` — superseded by a later doc that fully replaces it.** `specs/provably-fair.md` →
   `provably-fair-final.md`; the three-doc `audio-overhaul{,-generation,-wiring}` chain; the
   `rebrand-spacestroker` → `rebrand-space-stroker` → `rebrand-space-stroker-backend` chain; the
   `lp-space-stroker-relaunch` → `lp-redesign` → `lp-final-adjustments` chain; the many `orbit-*`
   iterations.
4. **`MERGE` — a `features/` doc that is near-duplicate prose of its `specs/` sibling**, or one of
   several docs describing one domain.
5. **`KEEP`** — everything else.

**Proposed target set for `features/`** (the inventory may refine it; the point is *one doc per
domain*, not one per shipped card):

| Domain doc | Absorbs |
| --- | --- |
| `games.md` | game lifecycle, typer pacing, lottery pot stacking + draw time, choice exclusivity, free-bet mode, house seed bet, pot display, characters-away |
| `money.md` | crypto commission, refund safeguards, the single ledger, payouts |
| `provably-fair.md` | provably-fair + provably-fair-final |
| `player.md` | player identity, wallet display name, customization modal, wallet-connect name prompt |
| `orbit.md` | the eight `orbit-*` docs |
| `frontend-ux.md` | app design refinement, mobile layout + game design, icons/menu, quick tour, money animations, scrollbars, graphics settings, light graphics, background lifecycle, off-page presentation |
| `stats-and-history.md` | game-type stats, games history, fair/stats menu, analytics, windowed stats performance, home dashboard |
| `embedding.md` | iframe-embedding + iframe-crypto-games |
| `infra.md` | terraform GCP deploy, terraform development deploy, MassTransit Postgres transport, Solana RPC connection, Helius gateway |
| `token-ops.md` | coinbase wallet funding, ghost wallet snipe, nightfall dashboard |
| `lp.md` | the four `lp-*` docs |
| `remove-operators.md` | **kept as its own doc** — it explains today's operator-free architecture |

Each merged doc opens with a one-paragraph "what this covers" and keeps per-behaviour subsections;
merging must not turn twelve precise docs into one vague one. Where a merged doc drops detail, it
links the surviving `specs/` file.

### 9.3 Phase 2 — STOP. User approves the inventory.

**Hard gate.** Present the inventory — counts per verdict, the full `DELETE` list, and the proposed
`features/` target set — and **wait for explicit approval**. Do not `git rm` anything before it.
Deleting ~100 knowledge-base files on criteria alone is not acceptable; the criteria decide the
proposal, the user decides the action.

### 9.4 Phase 3 — execute

1. Write the merged domain docs in `features/`.
2. `git rm` the approved deletions.
3. **Fix inbound references.** Every deleted or merged doc is linked from other docs. Search
   `features/`, `specs/`, `architecture.md`, `glossary.md`, `constraints.md`, `conventions/` and
   `AGENTS.md` for each removed filename and repoint or remove the link. A dead link is worse than
   the doc it replaced.
4. **`INDEX.md`, and only the map table.** Remove the rows for deleted docs; repoint rows for merged
   ones; add one row per new domain doc, in the existing style. Leave every other section — including
   **"Feature currently in flight"** — untouched (decision 3).
5. Delete `docs/kb-reduction-inventory.md`.

### 9.5 Phase 4 — audit

A link check must pass before the commit: extract every relative `.md` link from `INDEX.md` (and the
surviving docs) and assert the target exists. Report the before/after byte totals for `INDEX.md`,
`features/` and `specs/` — the card's whole point is the number going down.

⚠️ **Scan line-by-line, not by byte offset.** Files check out CRLF here, so an offset-based scan for
newline-delimited markers matches nothing and can loop until it exhausts RAM.

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None.**

### 10.2 Frontend (`monkify-front`)

**None.**

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `tools/secrets.example.json` | **Edit** — add the `nightfall` section (`token`/`jupiter`/`sell`); keep every existing section; placeholders only |
| 2 | `tools/secrets.nightfall.example.json` | **Delete** |
| 3 | `tools/.gitignore` | **Edit** — `/Receipts/`; keep legacy `*receipts*.json` + `/reports/` as leftover cover (§5.3) |
| 4 | `tools/SpaceStroker.Nightfall/Application/CliOptions.cs` | **Edit** — secrets default → `secrets.json`; receipts default → `Receipts/nightfall-receipts.json`; usage text |
| 5 | `tools/SpaceStroker.Nightfall/Infrastructure/SecretsFile.cs` | **Edit** — `NightfallSection`; re-path every validation message; missing-section fail-fast; replace the stale "own file" doc comment |
| 6 | `tools/SpaceStroker.Nightfall/Program.cs` | **Edit** — `secrets.Nightfall.*` reads |
| 7 | `tools/SpaceStroker.Nightfall/Application/{PollLoop,SellRunner,ApiEndpoints,ConsoleReport,NightfallState}.cs` | **Edit** — `secrets.Nightfall.*` reads (compile-driven) |
| 8 | `tools/SpaceStroker.Nightfall/Infrastructure/{Jupiter,Solana}/*.cs` | **Edit** — same, where they bind config |
| 9 | `tools/SpaceStroker.WalletFunder/Application/CliOptions.cs` | **Edit** — receipts default → `Receipts/receipts.json`; usage text; legacy-file **fatal** check |
| 10 | `tools/SpaceStroker.TokenSniper/Application/CliOptions.cs` | **Edit** — receipts → `Receipts/snipe-receipts.json`, reports → `Receipts/reports`; usage text; legacy-file **warning** |
| 11 | `tools/SpaceStroker.DbConsole/**` | **Move** from `deploy/tools/SpaceStroker.DbConsole/` (no code change) |
| 12 | `tools/SpaceStroker.Tools.sln` | **Edit** — add `SpaceStroker.DbConsole` |
| 13 | `tools/README.md` | **Edit** — 4-project table, one shared secrets file (+ the `nightfall` section), `Receipts/`, DbConsole row |
| 14 | `deploy/SpaceStroker.Deploy/**`, `deploy/SpaceStroker.Deploy.Tests/**` | **Move** up from `deploy/tools/` |
| 15 | `deploy/SpaceStroker.Deploy.sln` | **New** (renamed) — two projects, DbConsole entry dropped |
| 16 | `deploy/tools/SpaceStroker.DeployTools.sln` | **Delete** |
| 17 | `deploy/tools/SpaceStroker.Seeder{,.Tests}/` | **Delete** (untracked bin/obj only) |
| 18 | `deploy/secrets.seeder.json` | **Delete** (untracked) |
| 19 | `deploy/README.md` | **Edit** — layout block, `## DB console` path → `tools/SpaceStroker.DbConsole` |
| 20 | `deploy/docs/FIRST-DEV-DEPLOY.human.md` | **Edit** — line ~259 DB-console `cd` path |
| 21 | `deploy/docs/FIRST-DEV-DEPLOY.agent.md` | **Edit** — DbConsole path reference |
| 22 | `conventions/backend.md` | **Edit** — new "Meta-repo console tools" section (§6.6) |
| 23 | `AGENTS.md` | **Edit** — Quick context bullet for `monkify-meta` tooling |
| 24 | `features/*.md` | **Merge + delete** per the approved inventory (§9) |
| 25 | `specs/*.md` | **Delete** per the approved inventory (§9) |
| 26 | `INDEX.md` | **Edit** — map-table rows only; add a row for `specs/tools-refactor.md`; **"Feature currently in flight" untouched** |
| 27 | `specs/tools-refactor.md` | **New** — this document |

---

## 11. Unit tests

Project: the `tools/` solution's existing test projects — `SpaceStroker.Nightfall.Tests`,
`SpaceStroker.WalletFunder.Tests`, `SpaceStroker.TokenSniper.Tests`, `SpaceStroker.Deploy.Tests`.
**`SpaceStroker.Tests` (backend) is not run.**

| Test file | Test cases | Expected |
| --- | --- | --- |
| `SpaceStroker.Nightfall.Tests/Infrastructure/SecretsFileTests.cs` | Parse a shared-shape JSON with `solana` + `nightfall` + `wallets` | Binds `Nightfall.Token/Jupiter/Sell`; pool loads |
| " | `nightfall` section entirely absent | Throws naming the missing `nightfall` section and `secrets.example.json` |
| " | `nightfall.token.mint` is `REPLACE_ME` | Message contains **`nightfall.token.mint`**, not `token.mint` |
| " | `nightfall.jupiter.slippageBps` out of range; `nightfall.sell.maxConcurrentSells` < 1 | Messages carry the `nightfall.` prefix |
| " | Wallet entries carrying the funder's `SOLAmount` / sniper's `SupplyPercentage` | Unknown fields ignored; pool loads |
| " | File missing entirely | Message points at `tools/secrets.example.json` (not `secrets.nightfall.example.json`) |
| `SpaceStroker.Nightfall.Tests/Application/` (new `CliOptionsTests.cs`) | No `--secrets` / no `--receipts` | Defaults are `<toolsRoot>/secrets.json` and `<toolsRoot>/Receipts/nightfall-receipts.json` |
| " | `--secrets` / `--receipts` given | Overrides win |
| `SpaceStroker.WalletFunder.Tests/Application/CliOptionsTests.cs` | No flags | Receipts default is `<toolsRoot>/Receipts/receipts.json` |
| `SpaceStroker.WalletFunder.Tests/` (new legacy-receipts test) | New path absent + legacy `receipts.json` present | Fatal error naming both paths; **no send is planned** |
| " | New path present + legacy present | No error — the new file wins |
| " | Both absent | Clean first run |
| `SpaceStroker.TokenSniper.Tests/Application/CliOptionsTests.cs` | No flags | `Receipts/snipe-receipts.json` + `Receipts/reports` |
| " | Legacy `snipe-receipts.json` present, new absent | Warning, run proceeds |
| `SpaceStroker.Deploy.Tests/` (existing) | Full suite after the move | Green; `RepoPaths` still resolves `deploy/terraform/*` |
| new `SpaceStroker.DbConsole` assertion (or a `Deploy.Tests` case) | Walk-up from a `tools/SpaceStroker.DbConsole/bin/<cfg>/net8.0`-shaped path | Resolves `<root>/monkify-meta/deploy/secrets.<env>.json` |

Report the exact list of changed/added test files at the end of implementation.

---

## 12. Acceptance criteria

Copied 1:1 from the Trello **Acceptance Criteria** checklist (16 items).

1. Given a filled `tools/secrets.json` and no `secrets.nightfall.json` on disk, when
   `spacestroker-nightfall` is started with no `--secrets` flag, then it loads its
   token/jupiter/sell/wallets config from `tools/secrets.json` and starts normally.
2. Given `tools/secrets.json` declares `solana.rpcUrl` exactly once at the top level, when any of the
   funder, sniper or Nightfall reads its RPC endpoint, then all three resolve to that same value with
   no per-tool duplicate key.
3. Given a `tools/secrets.json` missing the section a tool needs, when that tool is run, then it exits
   with a clear message naming the missing section and pointing at `secrets.example.json`, printing no
   key material and no stack trace.
4. Given the refactor is complete, when the repo is inspected, then `tools/` contains exactly one
   tracked secrets template (`secrets.example.json`) covering every project's sections, and
   `secrets.nightfall.example.json` no longer exists.
5. Given no `--receipts` flag, when any `tools/` project writes run state or a report, then the file is
   created under `tools/Receipts/` and is ignored by git (`git status` stays clean after a run).
6. Given a legacy receipts file still sitting loose in `tools/` and an empty `tools/Receipts/`, when
   `spacestroker-fund-wallets` runs, then it reports that a legacy receipts file was found rather than
   treating the run as brand new, so no wallet can be double-funded.
7. Given the move is done, when `SpaceStroker.Tools.sln` is built, then it includes
   `SpaceStroker.DbConsole`, `SpaceStroker.DeployTools.sln` no longer references it, and no file
   remains under `deploy/tools/SpaceStroker.DbConsole/`.
8. Given DbConsole has been moved into the tools solution, when it is run against the development and
   then the production environment, then it still opens a read-write REPL on both the game and
   messaging DBs through the Cloud SQL Auth Proxy with its write-confirm prompt intact.
9. Given the seeder removal, when the repo is searched for `SpaceStroker.Seeder`, then no project
   folder, `bin`/`obj` leftover, `secrets.seeder.json` or doc reference to the deploy seeder tool
   remains, while the unrelated backend auto-bet/house-seed-bet docs are untouched.
10. Given the reorganisation is complete, when `deploy/` is inspected, then `SpaceStroker.Deploy` (plus
    its test project) is the only C# project left there and `deploy/README.md`'s layout block matches
    what is actually on disk.
11. Given an agent reads `conventions/backend.md` and `AGENTS.md` after the change, when it needs to
    know where the meta-repo console tools live, then both files state the `tools/` vs `deploy/` split,
    the single shared `tools/secrets.json`, the `tools/Receipts/` convention, and that these tools never
    run in Cloud Run or a container image.
12. Given the KB sweep is complete, when `features/` and `specs/` are listed, then every doc describing
    a removed capability (the operator/multi-tenant stack), every doc that is a one-off bug fix or QOL
    tweak, and every superseded duplicate has been deleted outright — not moved to an archive folder.
13. Given the surviving `features/` docs, when they are reviewed, then they are organised by domain
    boundary (one doc per domain rather than one per shipped card) and no two docs describe the same
    behaviour.
14. Given docs were deleted or merged, when every relative link in `INDEX.md` is resolved against the
    tree, then zero links point at a missing file and every surviving `features/` and `specs/` doc is
    still reachable from `INDEX.md`.
15. Given `INDEX.md` after the sweep, when it is diffed against the current version, then its section
    structure and prose style are unchanged and the "Feature currently in flight" section is
    byte-identical — only map rows for deleted or merged docs were removed or repointed.
16. Given all of the above, when the `tools/` solution is built and its tests are run, then every
    project compiles and all existing unit tests pass, with no change to what any tool does at runtime.

---

## 13. Manual QA

Run from `.worktrees/tools-refactor/monkify-meta`. **Never `--execute` anything** — these tools spend
real money; every step below is a dry/read-only path.

1. `dotnet build tools/SpaceStroker.Tools.sln` and `dotnet build deploy/SpaceStroker.Deploy.sln` —
   both clean.
2. `dotnet test tools/SpaceStroker.Tools.sln` — green. Note the count (191 across the solution before
   this card).
3. `dotnet run --project tools/SpaceStroker.Nightfall -- --help` — the `--secrets` line reads
   `tools/secrets.json`, the `--receipts` line reads `tools/Receipts/nightfall-receipts.json`.
4. With **no** `tools/secrets.json`: run each of the three money tools — each names
   `tools/secrets.example.json`, and none prints a key or a stack trace.
5. Copy `secrets.example.json` → `secrets.json` **unmodified** (placeholders only) and run
   `spacestroker-nightfall`: it must fail on `nightfall.token.mint is still a placeholder` — proving
   the section binds and the message carries the new path.
6. Delete the `nightfall` block from that copy and re-run: the missing-section message fires instead.
7. `spacestroker-snipe-token status` (read-only) writes nothing outside `tools/Receipts/`.
8. `git status` in `monkify-meta` — clean; nothing from `Receipts/` is offered.
   `git check-ignore -v tools/Receipts/receipts.json` confirms the rule.
9. `dotnet run --project tools/SpaceStroker.DbConsole -- --db game --env development` — the proxy
   starts and the REPL opens (the walk-up still finds `deploy/secrets.development.json`). `\q`.
10. Link audit (§9.5) — zero dead links in `INDEX.md`.

---

## 14. Trello / pipeline

- **Source card:** [#167 Tools refactor](https://trello.com/c/Co35YRGt/167-tools-refactor) — list
  **Specs created**; the **Implementation** section on the card carries branch, spec, worktree and level.
- **Branch:** `feature/tools-refactor` in `monkify-meta` only.
- **Next:** `/monkify-implement-feature Tools refactor` — note the **phase-2 approval gate** in §9.3.
- **After merge:** `/monkify-ship-feature Tools refactor`. **Level `Minor change`** ⇒ no
  `features/tools-refactor.md` doc is created; existing docs the change affects are corrected.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-08-11 | Initial spec from refined card #167. Two decisions settled during spec-writing: Nightfall's secrets merge recorded as an explicit reversal of `nightfall-dashboard.md` decision #4 (its blast-radius rationale was already moot — the sniper had put spend keys in the shared file), with a dedicated `nightfall` section; and `DbConsole` keeps `deploy/secrets.<env>.json` as a documented exception. Code exploration confirmed the DbConsole walk-up still resolves after the move, and that the `SpaceStroker.Seeder` source is already deleted. |
