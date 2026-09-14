# Hyper Roll — Landing Page and Knowledge-Base Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record the owner's A6 / §3.1 decisions in the spec, ship the Beast Mode deactivation SQL, and replace Beast Mode with Hyper Roll on the landing page (modes card, how-it-works copy, terms clause).

**Architecture:** Documentation and static-site edits only. `monkify-meta` carries the spec and a hand-run SQL script; `monkify-lp` (Astro) carries three copy edits and reuses the Beast Mode art until Hyper Roll has its own.

**Tech Stack:** Markdown, PostgreSQL (hand-run in Cloud SQL Studio), Astro + Vue SFC.

**Spec:** `monkify-meta/specs/hyper-roll.md`.

## Global Constraints

- `monkify-meta` work in `E:\Monkify\.worktrees\hyper-roll\monkify-meta` on `feature/hyper-roll` (off `main`).
- `monkify-lp` work in `E:\Monkify\.worktrees\hyper-roll\monkify-lp` on `feature/hyper-roll` (off `master`).
- The SQL is never run by an agent — a human runs it in Cloud SQL Studio.
- `GameStatus` is 1-based in SQL: `WaitingBets = 1`, `GameStarting = 3`, `InProgress = 4`.
- LP terms is a legal document: change only the two bullets named below.
- Commits end with:
  ```
  Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_016QNAKdJo28eauNmES75WZr
  ```
- Push after every commit: `git push origin feature/hyper-roll`.

---

### Task 1: Record the owner's decisions in the spec

**Files:**
- Modify: `specs/hyper-roll.md` (in the `monkify-meta` worktree)

- [ ] **Step 1: Replace decisions 17 and 20 and add 21–22**

In §3, replace the row that starts `| 17 | **Stake entry = free input + chips**` with:

```markdown
| 17 | **Stake entry = the console's green visor + bump chips** (owner picked mockup **A6**, 2026-09-14): `+0.01 / +0.05 / +0.1 / MAX`, no SET step — the lever places the bet. Lobby card shows "from <min> SOL". *(Refined as "reuse `OrbitAmountField`"; superseded by the mockup choice — only its validation rules carry over.)* |
```

Replace the row that starts `| 20 | **Implementation gate:**` with:

```markdown
| 20 | **Implementation gate — passed 2026-09-14.** The owner reviewed mockup rounds A/B/C, A1–A3 and A4/A5 and picked **A6** (https://claude.ai/code/artifact/6db62365-7929-4135-80cd-571802ac7ab0). |
| 21 | **Console + terminal = A6.** The terminal keeps today's screen line for line: `> CHOICES:` stays one `> word` per line in key order, and each line gains MULT · CHANCE · WIN (at the player's stake) under a dim header; 10× lines use the terminal's existing hot yellow + glow (`.result-win`), bold, with `< TOP` on desktop. `> BET` shows the stake; `> POT` becomes `> RTP: 95%`. The console keeps Option A's bottom: the stake typed in the visor, bump chips, the lever, and the 8 numbered keys each tagged with its multiplier (10× tags solid yellow). |
| 22 | **Spec §3.1 (S1–S14) approved by the owner, 2026-09-14.** |
```

- [ ] **Step 2: Correct code finding 11**

In §3.2 item 11, replace the clause

```markdown
`useGame.ts` uses `requiredAmount` as the stake at **four** sites (`:614` and `:661` mobile
    deep-link resume bodies, `:836` fee-headroom check, `:889` transfer amount)
```

with

```markdown
`useGame.ts` uses `requiredAmount` as the stake at `:836` (fee-headroom check) and `:889` (transfer
    amount) — the mobile deep-link resume already reuses that `amount` via `MobilePendingBet.amount`;
    `:614` and `:661` are embedded `postToParent` signals, of which only `bet-settled` (`:661`) should
    report the player's own stake *(corrected 2026-09-14)*
```

- [ ] **Step 3: Mark the §7.0 gate passed**

Directly under the heading `### 7.0 Gate — mockups before code`, insert:

```markdown
**Passed 2026-09-14 — the owner picked A6** (decisions 20–21). The paragraph below is kept as the record
of what the gate required.
```

- [ ] **Step 4: Rewrite §7.2 for A6**

Replace everything from the heading `### 7.2 Game console — \`src/components/game/GameConsole.vue\`` up to (not including) `### 7.3 Bet flow` with:

```markdown
### 7.2 Game console — `src/components/game/GameConsole.vue` (A6)

For `selectedGame.isHouseEdge`:

- **Keypad:** the 8 numbered key sprites read **`gameInstance.boardChoices`** (Position order = key order),
  not `selectedGame.presetChoices`. Each key carries a multiplier tag over its bottom edge; the 10× tags
  are solid yellow (`#fde047` on black) and one step larger.
- **Visor:** the green visor input is the **stake** (`inputmode="decimal"`, placeholder `min–max`), not a
  word. The chosen word is shown by the lit key and the terminal.
- **Bump chips:** `+0.01 / +0.05 / +0.1 / MAX` (steps above `maximumStake` are hidden). Desktop and the
  600–1023 px band render them in the left plate where the keyboard image sits; on phones they render in a
  row directly above the console band (`HyperRollStakeChips.vue`, mounted by `GamePage.vue`). MAX is
  `min(maximumStake, balance − 0.001)`.
- **Lever:** refuses (error twitch) until a board word **and** a valid stake are set. No SET step.
- Other types are unchanged.
```

- [ ] **Step 5: Rewrite §7.4 for A6**

Replace everything from the heading `### 7.4 Terminal — \`src/components/game/GameTerminal.vue\`` up to (not including) `### 7.5 Lobby card, art, guide, stats` with:

```markdown
### 7.4 Terminal — `src/components/game/GameTerminal.vue` (A6)

For house-edge rounds, pre-run block — same lines, same order, same type as today:

- `> GAME: <name>`
- `> BET: <stake> SOL` (the typed stake, or the minimum while none is typed), glowing like `> POT` today.
- `> RTP: 95% · BIGGER MULTIPLIER, RARER WORD` on desktop, `> RTP: 95%` on phones — **replaces `> POT:`**.
- `> CHOICES:` followed by one line per board word **in key order**: `> word`, multiplier, chance, and
  what the current stake wins, in aligned columns under a dim `MULT · CHANCE · WIN` header. 10× lines use
  `.result-win` (hot yellow + glow), bold, and end in `< TOP` on desktop.
- `> BETS PLACED:` unchanged.

Running/result block: no `> POT:` line; `> PAID:` renders the viewer's own payout from `result.payouts`
only when they won (omitted otherwise, never `0`); the near-miss dock is not rendered.
```

- [ ] **Step 6: Reword AC 14 and extend the file checklist**

In §13, replace acceptance criterion 14's text (from `Given the player is on a Hyper Roll round in the SPA` through `shows no \`N AWAY!\` row.`) with:

```markdown
14. Given the player is on a Hyper Roll round in the SPA, when the console and terminal render, then the 8
    numbered keys each show their multiplier (10× tags solid yellow), the terminal lists every board word in
    key order with multiplier, chance and win at the current stake (10× lines in the hot yellow with
    `< TOP`), the stake is typed in the visor with `+0.01 / +0.05 / +0.1 / MAX` chips, an amount outside
    `[min, max]` or below balance + fee headroom is refused before signing, and neither an `N AWAY!` row nor
    a `> POT:` line is shown.
```

In §11.2, replace row 2 (`GameConsole.vue`) and append rows 14–16:

```markdown
| 2 | `src/components/game/GameConsole.vue` | A6: board-driven keypad with multiplier tags, stake in the visor, chips in the left plate |
| 14 | `src/components/game/HyperRollStakeChips.vue` (new) | `+0.01 / +0.05 / +0.1 / MAX` stake chips |
| 15 | `src/pages/GamePage.vue` | Mount the chips above the phone console band |
| 16 | `src/utils/houseEdge.ts` (new) | Stake parsing, chance, payout, top-multiplier helpers |
```

- [ ] **Step 7: Add the revision-log row**

Append to the §17 table:

```markdown
| 2026-09-14 | Owner picked mockup **A6** and approved §3.1: decisions 17 and 20 revised, 21–22 added; §7.0 marked passed; §7.2, §7.4 and AC 14 rewritten for A6; code finding 11 corrected (`:614`/`:661` are embedded signals, not the deep-link resume); §11.2 gains `HyperRollStakeChips.vue`, `GamePage.vue`, `utils/houseEdge.ts`. |
```

- [ ] **Step 8: Commit**

```bash
git add specs/hyper-roll.md
git commit -m "Record the A6 console choice and the approved spec-time decisions for Hyper Roll."
git push origin feature/hyper-roll
```

---

### Task 2: Beast Mode deactivation SQL

**Files:**
- Create: `deploy/sql/beast-mode-deactivation.sql` (in the `monkify-meta` worktree)

- [ ] **Step 1: Write the script**

```sql
-- =====================================================================================
-- BEAST MODE DEACTIVATION — #183 Hyper Roll replaces it
--
-- Run ONCE, by hand, in Cloud SQL Studio against the `spacestroker` database.
-- `spacestroker-prod-pg` is PRIVATE-IP ONLY: cloud-sql-proxy fails with `instance does not
-- have IP of type "PUBLIC"`, so the console's SQL Studio is the route in.
--
-- ⚠️ WHY THIS IS SQL AND NOT CONFIG. Game types are seeded CREATE-ONLY BY NAME and no startup
--    self-heal ever writes `Active` (that is what stops a restart resurrecting a deactivated type).
--    Removing Beast Mode from GeneralSettings:Operator:DefaultGames only stops a FRESH database
--    seeding it; an existing database needs this script.
--
-- ⚠️ ORDER: run this BEFORE the Consumers deploy that ships Hyper Roll. That boot drops the 12h
--    `ActiveParameters` Redis blob unconditionally; run it after instead and Beast Mode stays in the
--    lobby until Consumers restarts again.
--
-- ⚠️ STATUSES ARE 1-BASED: WaitingBets = 1, GameStarting = 3, InProgress = 4. Beast Mode has been
--    ComingSoon since 2026-08-13, so no round should be open — the guard refuses (0 rows) if one is.
--
-- History, stats and provably-fair verification of Beast Mode's settled rounds are untouched.
-- =====================================================================================

-- ---------------------------------------------------------------------------------
-- PRE-CHECK (read-only, run on its own first): the row and any open round.
-- ---------------------------------------------------------------------------------
SELECT p."Id", p."Name", p."Active", p."ComingSoon", g."Id" AS game_id, g."Status"
FROM "GameParameters" p
LEFT JOIN "Games" g ON g."ParametersId" = p."Id" AND g."Status" IN (1, 3, 4)
WHERE p."Name" = 'Beast Mode';

-- ---------------------------------------------------------------------------------
-- DEACTIVATE. Expect "UPDATE 1". "UPDATE 0" means it is already inactive or a round is open.
-- ---------------------------------------------------------------------------------
UPDATE "GameParameters" p
SET "Active" = false, "UpdatedDate" = now()
WHERE p."Name" = 'Beast Mode'
  AND p."Active" = true
  AND NOT EXISTS (
      SELECT 1 FROM "Games" g
      WHERE g."ParametersId" = p."Id" AND g."Status" IN (1, 3, 4)
  );

-- ---------------------------------------------------------------------------------
-- VERIFY (after the Consumers deploy): GET https://api.spacestroker.com/games/active-types
-- lists "Hyper Roll" with "isHouseEdge": true and does NOT list "Beast Mode".
-- ---------------------------------------------------------------------------------
```

- [ ] **Step 2: Commit**

```bash
git add deploy/sql/beast-mode-deactivation.sql
git commit -m "Add the hand-run Beast Mode deactivation script for Hyper Roll."
git push origin feature/hyper-roll
```

---

### Task 3: Landing page — modes card, how-it-works copy, terms

**Files (in the `monkify-lp` worktree):**
- Modify: `src/components/GameModes.vue`
- Modify: `src/pages/index.astro`
- Modify: `src/pages/terms.astro`

- [ ] **Step 1: Replace the Beast Mode card**

In `src/components/GameModes.vue`, replace the object

```ts
  {
    key: "beast",
    name: "BEAST MODE",
    tag: "BET ON THE BEAST",
    blurb:
      "Back 1 of 8 beasts – the first one Edson spells out moons the whole pot. No typing.",
    // Static first frame by default; the GIF plays only while the card is active.
    img: asset(beastPoster),
    gif: asset(beastGif),
    accent: "cyan",
  },
```

with

```ts
  {
    key: "hyperroll",
    name: "HYPER ROLL",
    tag: "PICK A WORD · UP TO 10X",
    blurb:
      "Pick 1 of 8 words and set your stake – land it and the house pays your multiplier.",
    // #183 — reuses the Beast Mode character art until Hyper Roll has its own.
    // Static first frame by default; the GIF plays only while the card is active.
    img: asset(beastPoster),
    gif: asset(beastGif),
    accent: "cyan",
  },
```

and change the interface comment line `// Beast Mode (transparent character) stays contained + bottom-anchored.` to `// Hyper Roll (the transparent Beast Mode character) stays contained + bottom-anchored.`

- [ ] **Step 2: Update the how-it-works copy**

In `src/pages/index.astro`, replace

```ts
  { kind: "display", title: "Type your guess", body: "Punch your character run into the terminal — or tap a beast in Beast Mode. That's your horse." },
```

with

```ts
  { kind: "display", title: "Type your guess", body: "Punch your character run into the terminal — or pick a word in Hyper Roll. That's your horse." },
```

- [ ] **Step 3: Update the terms**

In `src/pages/terms.astro`, replace

```html
      <li>
        We take a house cut of winnings — <strong class="text-ink">currently 5%</strong> — taken only
        from profit above your own stake. A winner never nets less than they staked. The rate in force
        is always shown on the STATS panel in the game.
      </li>
```

with

```html
      <li>
        In Cosmic Sprint and Space Jackpot we take a house cut of winnings —
        <strong class="text-ink">currently 5%</strong> — taken only from profit above your own stake.
        A winner never nets less than they staked. The rate in force is always shown on the STATS panel
        in the game.
      </li>
      <li>
        Hyper Roll is played against the house: a winning pick pays your stake times the multiplier
        shown on that word, with no house cut. Its return to player is
        <strong class="text-ink">95%</strong>, shown on its STATS panel.
      </li>
```

- [ ] **Step 4: Build the site**

From `E:\Monkify\.worktrees\hyper-roll\monkify-lp`:

```bash
cmd //c "mklink /J node_modules ..\..\..\monkify-lp\node_modules"
npm run build
grep -rl "HYPER ROLL" dist | head -3
grep -rl "Beast Mode\|BEAST MODE" dist | head -3
```

Expected: build succeeds; the first grep lists at least one HTML file; the second prints nothing.

- [ ] **Step 5: Commit**

```bash
git add src/components/GameModes.vue src/pages/index.astro src/pages/terms.astro
git commit -m "Replace Beast Mode with Hyper Roll on the landing page and in the terms."
git push origin feature/hyper-roll
```

The `node_modules` junction is untracked (gitignored) and must not be committed.
