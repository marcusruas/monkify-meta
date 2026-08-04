# Space Stroker — "Orbit page restructure" — Implementation Spec

> **Status: Ready for implementation** — designed in a brainstorming session on 2026-08-04 against the
> **uncommitted** `feature/orbit-ui-overhaul` working tree (`monkify-front@993261e` + working changes,
> `monkify-meta@c6fc8bb`). Repos affected: **`monkify-front`**, **`monkify-backend`**, and this doc in
> **`monkify-meta`**.
>
> **Purpose:** [`orbit-ui-overhaul.md`](orbit-ui-overhaul.md) shipped a working but text-heavy `/orbit`
> page: three bands of prose, deposits and cash-outs still behind a modal opened *from* the page, no
> legible sense of order, and a grey panel the owner judged bland. This card keeps the overhaul's
> plumbing and **replaces the page's information architecture**: a collapse-to-dashboard step list, a
> one-row auto-play status banner, a violet/black panel, a strict four-colour semantic palette, and
> free-form integer deposits (which requires the **first backend change** in the Orbit UI line).

---

## 0. What this supersedes in `orbit-ui-overhaul.md`

That spec stays the authoritative record of the **identity layer, the console module, the mobile
deeplink work and the cancellable redirect** — none of which this card touches. The following
decisions in it are **superseded** and must not be re-derived from it:

| Superseded | Was | Now |
| --- | --- | --- |
| **D1** (page half only) | `/orbit` = three bands (readout strip / stop-rule pair / two equal cards), deposits + cash-out live only in the modal | `/orbit` = a five-step collapse-to-dashboard list that owns **every** Orbit action including deposit and cash-out. See §5. |
| **D6.1** | Violet is identity ONLY; hover/focus/borders/**every primary action** stay amber; a paused Orbit turns **amber** | Violet is identity **and** the active-step marker; **red** owns warnings including pause; amber narrows to **primary action buttons only**. See §4. |
| **§3.1** mitigation, item 3 | Seam row labelled `STOP RULES ▶` | Seam row keeps both live values **and** is labelled **`ORBIT SETTINGS ▸`**. The row — values included — remains the tap target; a bare button is still rejected. |
| **AC33** | "`monkify-backend` is diffed against `main` and has zero changes" | **Void.** §7 changes the deposit validator. `monkify-backend` gets a branch, a worktree and an integration test. |
| The grey-panel rule for `/orbit` | `.arcade-frame .arcade-frame--grey` | `.arcade-frame--orbit` — void-deep fill, violet-deep frame. See §4.4 and the rule amendment in §2. |
| The modal's game picker | `<select>` of `eligibleGameTypes` | **Removed.** The modal enables Orbit for the game the player is already in. Picking a *different* game happens on the page. See §6. |
| Deposit denominations | `<select>` of `depositOptions` `[1, 5, 10]` | Integer amount input + confirm, `1 … MaxDepositPerTransaction`. See §7. |

Everything else in `orbit-ui-overhaul.md` — §4 (tokens/mark/glyphs), §7.4 (console module), §7.8
(mobile deeplink), §7.9 (cancellable redirect) — is **unchanged and still binding**.

---

## 1. Summary

### 1.1 What ships

| Surface | Today (uncommitted overhaul) | After this card |
| --- | --- | --- |
| `pages/OrbitPage.vue` | 956 lines. Header + intro paragraph, signed-out 6-cell "What Orbit does" grid, band A (readout strip), band B (ladder + two rule editors + WHY BOTH strip), band C (recover + Telegram), phone counters, closing line. Two `Open Orbit panel` buttons. | A **five-step list** that collapses as it is satisfied, on a violet/black panel, with a one-row auto-play banner. **No button anywhere on the page opens a modal.** |
| Deposit | `<select>` of `[1, 5, 10]` inside the modal only | An **integer amount input + `CONFIRM`**, on the page **and** in the modal |
| Cash out | modal only | Page (**promoted out of the utility row whenever a balance exists**) and modal |
| `components/orbit/OrbitModal.vue` | compact 380: on/off + game picker + balance + deposit + cash out + seam row | **Two blocks**: turn Orbit on *for the current game*, and add money. Plus the status strip and the seam row. |
| Orbit's colour usage | violet = identity; amber = state, actions, hover, focus, pause | **Four hues, one job each** — green status, violet identity + active, red warning, amber actions. See §4. |
| Panel material | `.arcade-frame--grey` | `.arcade-frame--orbit` (void-deep + violet-deep frame) |
| Counters | their own band (desktop) + a repeated phone block | one thin **footer strip**, identical at every width |
| Backend deposit validator | `AvailableDepositOptions.Contains(amount)` | `amount` is a **whole number** in `[1, MaxDepositPerTransaction]` |
| `MaxDepositPerTransaction` | `10` | `100` (matches `MaxDepositPerDay`) |

### 1.2 The idea, in one sentence

**Orbit is an auto-play feature, so the page is the order you set it up in — and once it is set up,
that order collapses into the dashboard that tells you it is running.**

### 1.3 What does NOT change

Every Orbit **behaviour** is preserved: enable-with-game, deposit, cash-out, threshold/floor
save+clear, recover-a-deposit, Telegram link/verify/unlink, disable with force-cash-out and cool-off.
The three notification toasts fire unchanged. `useOrbitRedirect`'s poll cadence and detection logic are
untouched. The console module, the first-win upsell, the header row, the Orbit mark and the four
glyphs are untouched. **No daily cap, pool float cap, cool-off or cash-out-per-day rule is relaxed.**

---

## 2. Constraints (MANDATORY)

From `../constraints.md` and `../conventions/frontend.md`:

- **Worktree-per-repo** (`AGENTS.md` rule 6). Work lives in
  `.worktrees/orbit-ui-overhaul/{monkify-front,monkify-meta,monkify-backend}`. ⚠️ The backend
  worktree **does not exist yet** — it must be created (see §10). All three shared checkouts stay on
  `main`. Branch `feature/orbit-ui-overhaul` in every repo, pushed to `origin`.
- **Every overlay still goes through `ArcadeModal.vue`.** Teleport to `#app`, named `z-*` scale, no
  consumer `max-width`, no hand-rolled backdrop, no per-component Esc listener.
- **Every control takes its height from `--ctl-h-sm|md|lg`.** No control derives height from padding.
  Below 1023px the tokens step themselves up — no call-site class changes on mobile.
- **Do not stack Tailwind utilities on an arcade class.** Use the class as-is, use plain Tailwind for a
  differently-shaped control, or add a modifier in `arcade.css`.
- **Dither, never blur.** No `backdrop-filter`, no `blur()`, no infinite animation, no wide glow —
  `usePerfMode` forces `html.light-graphics` on every viewport below `lg`.
- ⚠️ **The black-panel rule is being amended, not broken.** `arcade.css` currently says the black panel
  is only for a surface that is *both brief-and-informative and light in tone*, and **"never for a
  warning about money"**. `/orbit` is a money page. Rather than bend that rule, this card adds a
  **third page material** — `.arcade-frame--orbit` — whose documented scope is *the Orbit feature
  only*. `--black` keeps its existing rule and its single consumer (the How-to-play tip block)
  untouched. **`conventions/frontend.md` → "Two panels" becomes "Three panels" and must be updated in
  the same commit**, or this card ships the dangling-rule problem #124 was filed to fix.
- **Cyan is not spent.** The focus ring stays the single cyan element, and Beast Mode keeps cyan as its
  archetype accent. `.arcade-frame--orbit` is violet precisely so cyan is left alone.
- **`constraints.md` §6 survives the copy pass verbatim in effect.** A forged session, an unknown nonce
  and a missing session stay **indistinguishable**. The deposit-amount refusal is a **client-side**
  validation of a **published** bound, so stating it leaks nothing (see §7.3).
- **Secrets/config:** mock/placeholder values only.
- **Integration test required.** `AGENTS.md` rule 8 — §7 changes money-accepting backend behaviour, so
  `SpaceStroker.Tests.Integration` gets a happy-path test. See §11.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| **R1** | **Collapse-to-dashboard, one layout.** Completed steps shrink to a one-line summary carrying their answer; the page is a wizard for a new player and a control panel for a returning one. Rejected: permanently-expanded steps (a returning player scrolls past four solved problems), and two distinct modes (two designs, plus a fuzzy boundary whenever a balance hits zero). |
| **R2** | **The step list is: `Game → Money → Stop rules → Telegram`.** `Sign in` is a **precondition, not a step** — it disappears once satisfied rather than holding a permanent ✓ at the top of the page. Signed-out is its own short screen (§5.2). |
| **R3** | **`Turn on` and `pick game` are ONE step.** The tick prompts for the game and saves both in a single patch; the backend refuses an enabled enrolment with no target. Splitting them would ship either a half-saved state or a step already satisfied on arrival. |
| **R4** | **`Recover a deposit`, `Cash out` and `Turn off` are utilities, not steps.** They act on a *running* Orbit, so they have no position in a setup order. They live in a footer strip — **except** cash-out, see R5. |
| **R5** | **Cash out is promoted out of the utility strip whenever `balance > 0`**, into its own row with the amount on the button. Money-out must never be harder to find than money-in. With a zero balance it drops back into the utility strip. ⚠️ Its button is a **secondary/outline** control, **not amber** — cashing out is available, not urged, and an amber button here would break §4.2's "a healthy running Orbit shows no amber". Amber is reserved for the **active step's** primary action. |
| **R6** | **The page owns every action; nothing on it opens a modal.** Deposit, cash-out, rule editing, recovery, Telegram and turn-off are all inline. The two `Open Orbit panel` buttons are deleted. |
| **R7** | **The modal is the short version of the page, not a second copy.** Exactly two blocks: *turn Orbit on for the game you are in*, and *add money*. No game picker, no rule editors, no Telegram, no recovery. See §6. |
| **R8** | **A one-row status banner opens the page**: `wallet ▶ orbit holds ▶ per round`, plus a rounds-left readout. It is the balance display and the explanation of auto-play in the same strip, so it **replaces prose rather than adding to it**. Rejected: a full loop diagram (too tall on a phone), and no banner at all (nothing on the page would say "auto-play", which is the job the deleted prose was doing). |
| **R9** | **Colour encodes meaning, never completion.** Four hues, one job each — §4.1. The first draft encoded *done* as green pills and produced four accents competing on one screen; the fix is both the semantic rule **and** a weight rule (§4.2). |
| **R10** | **Panel material = `.arcade-frame--orbit`** (void-deep fill, violet-deep 2px frame). Rejected: grey (the owner's "bland"), and `--black` with a cyan frame (breaks the documented black-panel rule *and* spends cyan). |
| **R11** | **Deposits become whole-number amounts in `[1, MaxDepositPerTransaction]`, validated on BOTH sides**, and `MaxDepositPerTransaction` rises to `100`. The ladder is product policy, not a safety property — the daily cap, pool float cap, cool-off and one-cash-out-per-day rules all survive untouched. See §7. |
| **R12** | **The seam row survives, relabelled `ORBIT SETTINGS ▸`.** It keeps **both live rule values**, and the row *including its values* is the tap target. `orbit-ui-overhaul.md` §3.1's reasoning is unchanged: a bare `SETTINGS ▸` button would teach nothing, and the floor's exposure must be legible from inside a live round. |

### 3.1 The §3.1 trade-off, re-examined and still accepted

`orbit-ui-overhaul.md` §3.1 accepted that the cash-out **floor** is readable but not editable
mid-round, and named Option A's shape (both editors in the modal) as the sanctioned fix if it proved
wrong. **This card does not change that**, and deliberately did not take the opportunity to:

- The modal is being made **smaller**, not larger — adding two rule editors to it would undo R7.
- The mitigation is *strengthened*, not weakened: the seam row now renders an unset floor as
  **`no floor` in red** (§4.1) rather than a grey `NOT SET`, so the exposure is louder than before.

⚠️ The residue is unchanged and still knowingly accepted: on a phone, a player on a losing run must
leave the round to set a floor. If that proves wrong in the wild, the fix remains Option A's shape.

---

## 4. The colour system

### 4.1 Four hues, one job each

| Hue | Token | Owns | Never |
| --- | --- | --- | --- |
| **Green** | `--color-live` | Active / running / satisfied. The ✓ pips, the `● running` tag, `linked`. | Asks for anything. It is status, so it is never an action or a warning. |
| **Violet** | `--color-orbit`, `--color-orbit-deep` | Orbit identity **and** "you are here". Mark + panel frame at `-deep`; the **open step** at full. | State that is not "current". Never a value, never a button fill. |
| **Red** | `oklch(0.8 0.16 25)` | Warning / error. `no floor`, `paused`, `not enough for a round`, cool-off, failed deposit. | Decoration. If nothing is wrong, there is no red on screen. |
| **Amber** | `--color-primary` | The **active step's** primary action button only — the filled `CONFIRM` / `TURN ORBIT ON`. | Status, borders, hover tints, headings, the page title, or any always-available action. `CASH OUT` is an outline control (R5), not amber. |

**The title `ORBIT` is `--color-ink` (white).** The mark beside it carries the violet; the title is a
label, not a status.

### 4.2 The weight rule — this is what actually prevents the salad

The first draft used the correct *number* of hues and still read as noise, because **every value was a
filled, bordered pill**. Therefore:

- **Colour lives in small marks only** — a pip, a 3px left bar, one status tag, one filled button.
- **Every value is plain ink.** `Cosmic Sprint · 1.00`, `12.40`, `20 / 5`, `linked` — no chip, no fill,
  no border. The single exception is `no floor`, which is red **text** inside an otherwise ink value.
- **A healthy running Orbit has no amber and no red anywhere.** That is what makes either mean
  something when it appears.

### 4.3 The one hue clash, and how it is resolved

Violet is both the identity (the panel frame) and "you are here" (the open step). An open step framed
in violet inside a violet-framed panel flattens. **Resolved by intensity, not hue:** the frame and
mark sit at `--color-orbit-deep` (L 0.5), the open step takes `--color-orbit` (L 0.72). Same hue,
unambiguously different weight. Do not "fix" this by giving the active step a different colour.

### 4.4 `.arcade-frame--orbit` — `src/assets/styles/arcade.css`

Declared beside `--grey` and `--black`, with its scope documented in the same style as theirs:

```css
/* The ORBIT PANEL as a page surface — void-deep behind a 2px violet-deep frame.
   SCOPE IS DELIBERATE: the /orbit page ONLY. It exists because the money page needed the depth of
   the black panel WITHOUT breaking that panel's rule (black is brief-and-informative and light in
   tone, and NEVER a warning about money — /orbit carries a stop-loss). Rather than bend --black,
   Orbit got its own material in its own identity colour.
   The frame is --color-orbit-DEEP, not --color-orbit: the OPEN STEP inside takes the full-intensity
   violet, and a full-intensity frame would flatten it (see specs/orbit-page-restructure.md §4.3).
   Cyan was rejected: it is the focus ring and Beast Mode's archetype accent, and Orbit already owns
   a colour. NO GLOW — every phone runs html.light-graphics, which strips one. */
.arcade-frame--orbit {
  background-color: var(--color-void-deep);
  border-color: var(--color-orbit-deep);
}
```

⚠️ `conventions/frontend.md` → **"Two panels"** must become **"Three panels"** in the same commit.

---

## 5. The `/orbit` page

### 5.1 Structure

```
┌───────────────────────────────────────────────── .arcade-frame--orbit ──┐
│ ◉ ORBIT (white)                                   ● running   [◀ BACK]  │  header
├─────────────────────────────────────────────────────────────────────────┤
│ WALLET 124.00  ▶  ORBIT HOLDS 12.40  ▶  PER ROUND 1.00   12 rounds left │  status banner (R8)
├─────────────────────────────────────────────────────────────────────────┤
│ ✓  Game         Cosmic Sprint · 1.00                                  ▸ │  step 1 (R3)
│ ✓  Money        12.40                                                 ▸ │  step 2
│ ✓  Stop rules   20 / 5                                                ▸ │  step 3
│ ✓  Telegram     linked                                                ▸ │  step 4
├─────────────────────────────────────────────────────────────────────────┤
│ Take it all back to your wallet            [ CASH OUT 12.40 ]           │  R5, only when balance > 0
├─────────────────────────────────────────────────────────────────────────┤
│ WON 4.00   BETS 37                        [Recover] [Turn off]          │  footer strip
└─────────────────────────────────────────────────────────────────────────┘
```

**Exactly one step may be open at a time.** Opening a step collapses the others. The open step takes
the full-violet left bar + header tint; collapsed steps are neutral with a green ✓ pip and their
answer in ink.

### 5.2 The states, and how they are produced

All states are **the same four rows re-weighting themselves**. There is no second layout, no route
change and no mode flag.

| State | Condition | What renders |
| --- | --- | --- |
| **Signed out** | `!hasSession` | Short screen: sign-in button, one sentence on what signing costs, and the "what Orbit does" explainer. The step list is not rendered. |
| **Not enrolled / off** | `!orbit.enabled` | Step 1 is **open and active**; steps 2–4 are collapsed and locked with the reason. |
| **Running** | `enabled && !isPausedForFunds` | All four collapsed. Green `● running` tag. **No amber, no red on screen.** Cash-out row present if `balance > 0`. |
| **Paused — no funds** | `isPausedForFunds` | Red `● paused` tag, red banner readout naming the numbers (`0.40` vs `1.00 per round`). **Step 2 un-collapses in place, keeping its number**, and becomes the active step. |
| **Floor unset** | `!hasCashOutFloor` | Step 3 gets a red left bar and a `!` pip; its collapsed value reads `20 / no floor` with `no floor` in red. Everything else stays green. |
| **Off — cool-off** | `enableLockedUntil` | Grey `● off` tag, red `cooling off · Nh left`. Steps 1–2 lock with the remaining time on them. **Steps 3–4 stay editable** — rules survive a disable, and this is exactly when a player fixes the floor that burned them. |

### 5.3 Copy budget

The page's total prose is capped at **one sentence per open step**, plus the signed-out explainer.
Everything the deleted paragraphs said is now carried by the banner's live numbers. Specifically
**deleted**: the header intro paragraph, band A's "Deposits and cash-outs stay in the Orbit panel…",
the `WHY BOTH` strip, the two per-rule ON/OFF explanation paragraphs, the closing consequential line,
and the six-cell explainer grid on the signed-*in* view (it stays on signed-out only).

---

## 6. The mid-game modal — `OrbitModal.vue`

Two blocks, plus a status strip and the seam row. Rendered at the existing compact width.

| Block | When | Content |
| --- | --- | --- |
| **Turn Orbit on** | `!orbit.enabled` | One sentence naming **the game the player is already in** and its stake, then a filled `TURN ORBIT ON`. **No `<select>`** — the game is `activeGameType`, resolved from the route. |
| **Add money** | always rendered | Integer input + `CONFIRM`. **Disabled with the reason stated** when `!canDeposit` — "Turn Orbit on first — it can't hold money it can't play with." A disabled control that explains itself is not a dead button. |
| **Status strip** | `orbit.enabled` | `ORBIT HOLDS ▶ PER ROUND` + rounds-left. Same shape as the page banner minus the wallet segment. |
| **Seam row** | always | `Stop rules  20 / no floor        ORBIT SETTINGS ▸` — the whole row is the tap target to `/orbit` (R12). |

The paused state is the running panel with a red tag, a red `not enough for a round`, and the Add
money block already open. **No new layout.**

⚠️ The modal must **not** render on a game type where `allowOrbit` is false; its entry point (the
console module) already gates on this.

---

## 7. Free-form integer deposits

### 7.1 Why this needs a backend change

`CreditOrbitDepositHandler` currently refuses any amount outside the ladder:

```csharp
if (!orbit.AvailableDepositOptions.Contains(amount) || amount > orbit.MaxDepositPerTransaction)
    return Reject($"{amount} is not an offered denomination, or exceeds the per-transaction cap");
```

The deposit is **two-phase** — `useOrbit.deposit` calls `sendToken` **first**, then credits. A
front-only amount input would therefore let a player transfer `7` USDC on-chain and *then* be refused,
with the Recover-a-deposit escape hatch refusing it too (same ladder). **Money out, balance unchanged.**

### 7.2 Backend (`monkify-backend`)

- `OrbitSettings.MaxDepositPerTransaction` default `10m` → **`100m`**. `MaxDepositPerDay` stays `100m`.
- `CreditOrbitDepositHandler` denomination check becomes:

  ```csharp
  if (amount < 1m || amount != decimal.Truncate(amount) || amount > orbit.MaxDepositPerTransaction)
      return Reject($"{amount} is not a whole number within the per-transaction cap");
  ```

- `GetOrbitHandler` keeps returning `DepositOptions` (the front still uses it for **quick-fill chips**,
  §7.3) and **additionally** returns `MinimumDeposit = 1` and `MaxDepositPerTransaction`.
- `DepositOptions` / `AvailableDepositOptions` are **not deleted** — the ⚠️ binder-append hazard
  documented on that property stays exactly as it is.
- **Do not touch** the idempotency check, the cool-off refusal, the daily cap, the pool float cap, or
  the vague-`Reject` policy.

### 7.3 Frontend (`monkify-front`)

- A shared `OrbitAmountField.vue` — integer input + confirm — used by **both** the page's Money step
  and the modal's Add money block. One implementation, two mounts.
- **The front validates before any transfer starts.** `useOrbit.deposit` gains the same guard as its
  existing cool-off / off / self-deposit guards, which all deliberately `throw` **before** `sendToken`:

  ```ts
  if (!Number.isInteger(amount) || amount < 1 || amount > orbit.value.maxDepositPerTransaction) {
    lastError.value = `Deposits are whole numbers from 1 to ${orbit.value.maxDepositPerTransaction}.`;
    throw new Error(lastError.value);
  }
  ```

  This message is **allowed to be specific** — it states a published bound and reveals nothing about a
  session, a nonce or another player, so `constraints.md` §6 is untouched.
- The confirm button is disabled until the amount is valid, so the message is a backstop, not the
  primary UX.
- **`Recover a deposit` takes the same field.** Its `<select>` of `depositOptions` is removed — once
  deposits are free-form, a ladder select cannot recover the deposit it exists to rescue.
- **No quick-fill chips.** `depositOptions` is no longer rendered anywhere in the front — the ask was
  "a simple input with a confirm button", and chips would reintroduce the ladder the card is removing.
  The field is the only way to name an amount. (The DTO keeps returning the array; the front ignores
  it. Deleting it server-side is out of scope — see §7.2's note on the binder hazard.)

---

## 8. Out of scope

- The console module, the first-win upsell, the header row, the Orbit mark and glyphs (shipped in
  `orbit-ui-overhaul.md`, untouched here).
- The mobile deeplink work and the cancellable redirect (same).
- Any change to the auto-bet engine, the payout path, or the notification channel.
- Raising `MaxDepositPerDay`, `PoolFloatCap`, or relaxing the cool-off / one-cash-out-per-day rules.
- An app-wide icon system (still the dangling-rule condition #124 was filed to fix).
- Editing the stop-rule **floor** from inside the modal (§3.1 — knowingly not taken).

---

## 9. File checklist

### 9.1 Backend (`monkify-backend`)
- `src/SpaceStroker.Domain/Configs/Entities/OrbitSettings.cs` — cap `10m` → `100m`
- `src/SpaceStroker.Infrastructure/Handlers/Orbit/CreditOrbitDeposit/CreditOrbitDepositHandler.cs` — range check
- `src/SpaceStroker.Infrastructure/Handlers/Orbit/GetOrbit/GetOrbitRequest.cs` + `GetOrbitHandler.cs` — expose bounds
- `src/SpaceStroker.Tests/UnitTests/Api/OrbitSettingsBindingTests.cs` — cap assertion
- `src/SpaceStroker.Tests/UnitTests/Handlers/Orbit/…` — denomination tests → range tests
- `src/SpaceStroker.Tests.Integration/OrbitFlowTests.cs` — new happy-path test (§11)

### 9.2 Frontend (`monkify-front`)
- `src/pages/OrbitPage.vue` — rebuilt (§5)
- `src/components/orbit/OrbitModal.vue` — reduced to two blocks (§6)
- `src/components/orbit/OrbitStepRow.vue` — **new**, the collapse/expand row
- `src/components/orbit/OrbitStatusBanner.vue` — **new**, R8
- `src/components/orbit/OrbitAmountField.vue` — **new**, §7.3
- `src/hooks/useOrbit.ts` — deposit bound guard, bounds from the DTO, `roundsLeft` derivation
  (`activeGameType.requiredAmount > 0 ? Math.floor(balance / requiredAmount) : null`; renders as
  `— rounds left` when `null`, and drives `isPausedForFunds`'s copy when `0`)
- `src/types/orbit.ts` — `minimumDeposit`, `maxDepositPerTransaction`
- `src/assets/styles/arcade.css` — `.arcade-frame--orbit` (§4.4)

### 9.3 Meta (`monkify-meta`)
- `specs/orbit-page-restructure.md` — this file
- `specs/orbit-ui-overhaul.md` — supersession banner pointing here (§0)
- `conventions/frontend.md` — "Two panels" → "Three panels"; palette rule §4.1
- `features/orbit-custodial-wallet.md` — deposit amounts are no longer a ladder
- `constraints.md` — note that the deposit-bound message is a sanctioned §6 exception

---

## 10. Repos, branches, worktrees

`feature/orbit-ui-overhaul` already exists in `monkify-front` and `monkify-meta`, with worktrees at
`.worktrees/orbit-ui-overhaul/`. **`monkify-backend` has neither** and must get both:

```bash
# from <workspace-root>, after the collision pre-flight in constraints.md → "Git"
git -C monkify-backend fetch origin
git -C monkify-backend worktree add -b feature/orbit-ui-overhaul \
    ../.worktrees/orbit-ui-overhaul/monkify-backend origin/main
git -C .worktrees/orbit-ui-overhaul/monkify-backend push -u origin HEAD
```

⚠️ **The front worktree currently holds ~1,900 lines of uncommitted work** from
`orbit-ui-overhaul.md`. Commit it as a checkpoint **before** starting this card, so the restructure is
a reviewable diff rather than an indistinguishable blob.

---

## 11. Tests

**Backend — mandatory (`AGENTS.md` rule 8).** This card changes money-accepting behaviour.

- **Integration (`SpaceStroker.Tests.Integration/OrbitFlowTests.cs`)** — happy path, asserted end to
  end: enable Orbit → deposit a **non-ladder whole amount (e.g. 7)** → assert the credit is accepted
  and the ledger balance is exactly 7 → assert a **second** deposit of 7 with the same signature is
  idempotent → assert the daily cap still refuses the amount that crosses 100.
- **Unit** — `1` accepted, `100` accepted, `0` refused, `101` refused, `7.5` refused, `-3` refused;
  `AvailableDepositOptions` binding test updated for the new cap.
- Run with **`run_tests.bat`**, not bare `dotnet test` (the script excludes the `ProductionGames`
  Monte Carlo suite by design). Report which unit tests changed.

**Frontend.** No test framework in the repo. Verification is `yarn lint` + `yarn build` clean, plus
the manual QA below.

---

## 12. Acceptance criteria

1. `/orbit` renders four step rows; exactly one is open at a time.
2. A satisfied step collapses to one line carrying its answer in **ink**, with a green ✓ pip.
3. The status banner shows `wallet ▶ orbit holds ▶ per round` with live values and a rounds-left readout.
4. **No control on `/orbit` opens a modal.** Deposit, cash-out, rule editing, recovery, Telegram and turn-off are all inline.
5. Cash-out has its own row with the amount on the button whenever `balance > 0`, and drops to the utility strip at zero.
6. A **running, fully-configured** Orbit renders **no amber and no red** anywhere on the page.
7. Every value is plain ink; the only coloured value is `no floor` (red).
8. The title `ORBIT` is `--color-ink`; the mark is violet.
9. The page uses `.arcade-frame--orbit`; `.arcade-frame--black` is unchanged and its one consumer still renders identically.
10. `conventions/frontend.md` documents three page panels.
11. Paused-for-funds un-collapses **step 2 in place**, keeping its number — no separate screen.
12. In the cool-off, steps 1–2 lock with the time shown and steps 3–4 remain editable.
13. The modal renders exactly two blocks; it contains **no game `<select>`**; it names the current game.
14. Depositing `7` succeeds end to end. Depositing `0`, `101` or `7.5` is refused **before any transfer starts**.
15. `Recover a deposit` accepts an arbitrary whole amount.
16. The seam row shows both live rule values and is labelled `ORBIT SETTINGS ▸`; the whole row navigates.
17. `MaxDepositPerTransaction` is `100`; `MaxDepositPerDay`, `PoolFloatCap`, the cool-off and the one-cash-out-per-day rule are unchanged.
18. `yarn lint` and `yarn build` are clean; `run_tests.bat` passes.

---

## 13. Manual QA (local Docker stack)

Backend on `:6001`, front dev server from the worktree. Free-bets operator via `POST /dev/launch-token`.

1. Signed out → `/orbit` shows the sign-in screen and the explainer, no step list.
2. Sign in → step 1 open, steps 2–4 locked with reasons.
3. Turn on for Cosmic Sprint → step 1 collapses to `Cosmic Sprint · 1.00`, step 2 opens.
4. Deposit `7` → accepted; banner shows `7.00` and `7 rounds left`.
5. Let the balance fall below `1.00` → red `paused`, step 2 un-collapses in place.
6. Clear the floor → step 3 shows `20 / no floor`, red bar, red word only.
7. Set both rules → page shows **no amber and no red**.
8. Cash out → row disappears at zero balance, `Cash out` returns to the utility strip.
9. Turn off → cool-off; steps 1–2 locked with the time, 3–4 still editable.
10. On `/game/play` with an Orbit-eligible type: modal shows two blocks, names Cosmic Sprint, no `<select>`; seam row reads both rules and navigates.
11. Deposit `0`, `101`, `7.5` → refused with **no wallet prompt** (verify no transaction is signed).
12. Phone width: banner wraps, one step open, no horizontal scroll, no blur.

---

## 14. Revision log

| Date | Change |
| --- | --- |
| 2026-08-04 | Written. Supersedes `orbit-ui-overhaul.md` D1 (page half), D6.1, AC33, the grey material, the modal's game picker and the deposit ladder — see §0. |
