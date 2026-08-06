# Space Stroker — "Orbit new UI" — Implementation Spec

> **Status: Ready for implementation** — designed in a brainstorming session on 2026-08-06 against
> `feature/orbit-new-ui` (`monkify-front@66e4df3`, `monkify-backend@33e396e`, `monkify-meta@3ebee0e`),
> from two owner mockups supplied the same day.
> Repos affected: **`monkify-front`** and this doc in **`monkify-meta`**.
> **No backend change — `monkify-backend` gets no new work from this card.**
>
> **Purpose:** [`orbit-page-restructure.md`](orbit-page-restructure.md) rebuilt `/orbit` as a
> **collapse-to-dashboard step list**. The modal has since been redesigned (`66e4df3`) and absorbed
> turn-on and add-money, which left the page's step metaphor describing a setup order the modal no
> longer defers to. This card replaces that structure with **tabs**, moves the run/stop/off controls
> **outside the card** where they govern everything, and steps the whole page up one size class so it
> stops reading like a 380px panel stretched to 1000px.

---

## 0. Base branch — read this before anything else

⚠️ **This card is NOT based on `main`.** `feature/orbit-new-ui` was cut from
**`feature/orbit-ui-overhaul`** on 2026-08-06, in all three repos, because that branch carries both
the `/orbit` page this card rewrites and the redesigned modal this card must stay consistent with.

At branch time the base commits were `monkify-backend@cbe6f97`, `monkify-front@4d3cf27`,
`monkify-meta@3ebee0e`. The branch's ancestry therefore contains **#141 (UI overhaul)** and
**#150 (feedback adjustments)**.

⚠️ **Merge-order hazard.** Merging `feature/orbit-new-ui` into `main` **before**
`feature/orbit-ui-overhaul` lands silently drags the entire UI overhaul *and* the feedback
adjustments in with it, because both are in this branch's ancestry. `feature/orbit-ui-overhaul` must
land first. This is **AC18**.

**Work already committed on this branch and NOT part of this card** (it came from the session that
cut the branch, and reviews separately):

| Repo | Commit | What |
| --- | --- | --- |
| `monkify-backend` | `33e396e` | `AutoPickChoice` — the automatic word becomes an explicit opt-in, defaulting false. Migration `20260806034943_AddOrbitAutoPickChoice`, plus `OrbitAutoPickChoiceFlowTests`. 834 unit tests green. |
| `monkify-front` | `66e4df3` | The Orbit panel's type scale (46px/14px titlebar, `.orbit-section-title`, `.orbit-card-title`) and the auto-pick checkbox. `yarn lint` clean. |

---

## 1. Summary

### 1.1 What ships

| Surface | Today (`orbit-page-restructure`) | After this card |
| --- | --- | --- |
| `pages/OrbitPage.vue` | 1,379 lines. Four `OrbitStepRow`s that collapse to a one-line answer, exactly one open at a time, plus cash-out and recover sections below them. | A **~250-line shell**: header, stat strip, tab bar, active panel, control trio, bet log. Every panel and its handlers live in their own file. |
| Setup navigation | A **step list** — an order you work through, collapsing as it is satisfied | **Five tabs** — `① GAME · ② MONEY · ③ STOP RULES · ④ RECOVER · ⑤ TELEGRAM`. All are reachable at any time; none collapses. |
| Run / stop / turn off | Turn-off buried in a utility strip; pause reachable from the status banner | A **trio of controls below the card**: `▶ RUN ORBIT`, `⏸ STOP ORBIT`, `⏻ TURN ORBIT OFF` |
| Readout | `OrbitStatusBanner` (`wallet ▶ orbit holds ▶ per round` + rounds-left) | A **stat strip** — `ORBIT HOLDS` · `PER ROUND` · `WALLET BALANCE` — violet-framed, 32px values. `OrbitStatusBanner` stays for **the modal**, untouched. |
| Type scale | The modal's scale, used as-is at 1000px | The same **ranking**, every step **one size up** (§4), scoped under `.orbit-page` |
| Cash out | Its own promoted row whenever a balance exists | Inside `② MONEY`, beside add-money — the two everyday money moves together |
| Recover a deposit | A section under the step list | `④ RECOVER`, on its own — a repair path, not a daily action |
| `components/orbit/OrbitStepRow.vue` | The collapse unit | **Deleted.** Its only consumer is the page. |

### 1.2 The idea, in one sentence

**The step list said "here is the order you set Orbit up in"; the tabs say "here is everything Orbit
can do, and the three buttons under the card say what it is doing right now."**

### 1.3 What does NOT change

Every Orbit **behaviour**. Deposit, cash-out (partial, `MinimumCashOut` → balance), threshold and
floor save/clear, recover-a-deposit, Telegram link/verify/unlink and its three toggles, disable with
force-cash-out and cool-off, the auto-pick opt-in, the pause state, the bet log. `useOrbit.ts` gains
**no new state** — this card is a re-presentation of what it already exposes. The three #123 toasts,
`useOrbitRedirect`'s cadence and the console module are untouched. **`monkify-backend` is diffed
against its branch point and has zero changes from this card (AC17).**

---

## 2. Constraints (MANDATORY)

From `../constraints.md`, `../conventions/frontend.md` and `AGENTS.md`:

- **Worktree-per-repo** (`AGENTS.md` rule 6). Work lives in
  `.worktrees/orbit-ui-overhaul/{monkify-front,monkify-meta}` on branch `feature/orbit-new-ui`,
  already pushed to `origin`. The shared checkouts stay on `main`.
- ⚠️ **Three components are SHARED with the redesigned modal and must not be reshaped:**
  `OrbitStatusBanner.vue`, `OrbitAmountField.vue` and `OrbitWordField.vue` are each imported by
  **both** `OrbitModal.vue` and `OrbitPage.vue`. The tabs **consume them as they are**. Changing
  their props, slots or internal layout regresses the panel shipped in `66e4df3`. If a tab needs a
  different shape, it wraps rather than edits. `OrbitStepRow.vue` has exactly one consumer (the page)
  and is therefore safe to delete.
- **Every control takes its height from the `--ctl-h-*` scale**, never from padding. The 48px fields
  are the existing `--ctl-h-lg`. ⚠️ The 56px trio buttons need a **new `--ctl-h-xl`**, added to
  `src/assets/styles/colors.css` beside the other three and documented in the same commit — this card
  **extends** the control-sizing scale rather than bending it, the same way `orbit-page-restructure.md`
  added a third panel material rather than bending `--black`. Note that **`--ctl-h-lg` has no mobile
  step-up** (only `sm` and `md` step up below `lg`, to 40px and 44px); `--ctl-h-xl` deliberately
  matches that and stays 56px at every width, because the trio stacks full-width on phones and takes
  its touch target from width, not height.
- **Do not stack Tailwind utilities on an arcade class.** Use the class as-is, use plain Tailwind for
  a differently-shaped control, or add a modifier in `arcade.css`.
- **Dither, never blur.** No `backdrop-filter`, no `blur()`, no infinite animation, no wide glow —
  `usePerfMode` forces `html.light-graphics` on every viewport below `lg`.
- **The page keeps `.arcade-frame--orbit`.** The third page material added by
  `orbit-page-restructure.md` stands; this card does not add a fourth. `conventions/frontend.md`
  still documents three page panels.
- **Colour keeps one job each** (`orbit-page-restructure.md` §4): green = running/OK, violet =
  identity and the active tab, red = warning, amber = primary action buttons only. The trio in §5.2
  is the one place green and red appear as **button fills**, and §5.2 states why that does not
  reopen the salad.
- **Cyan is not spent.** The focus ring stays the single cyan element.
- **`constraints.md` §6 survives the copy pass verbatim in effect.** A forged session, an unknown
  nonce and a missing session stay indistinguishable.
- **Secrets/config:** mock/placeholder values only.
- **No integration test required.** `AGENTS.md` rule 8 scopes the requirement to **new features**;
  this is a re-presentation of shipped behaviour with zero backend change. The bar is §9.

---

## 3. Resolved product decisions

| # | Decision | Ruling |
| --- | --- | --- |
| **D1** | Steps or tabs? | **Tabs**, five of them, numbered pips retained. Owner call from the mockups. |
| **D2** | The round-budget stepper in mockup 1 (`ROUNDS TO PLAY`) | **Dropped.** No `roundsToPlay` exists in `OrbitSettings`; it needs a column, a migration, planner logic and an integration test, which makes it a feature card, not this one. It cannot be faked with `cashOutFloor` — a floor is balance-based, so any win pushes it out of reach and Orbit sails past the target round. Owner: *"forget that"*. |
| **D3** | `× CLOSE` / `Press ESC to close` in the mockups | **Dropped.** Modal furniture on a route. The page keeps its existing `◀ Back` control, which already occupies that corner. Owner: *"some things in the mockup don't make sense. like close buttons"*. |
| **D4** | Where does turn-off live? | **A third button in the trio**, outside the card. Rejected: folding it into `STOP ORBIT`, which would re-couple what #150 deliberately split — every casual stop would force a cash-out and start the cool-off. |
| **D5** | `MONEY` vs `RECOVER & DEPOSIT` (they overlapped in the mockup) | **`MONEY` owns both everyday moves** — add money *and* cash out. Tab 4 is renamed **`RECOVER`** and holds only the signature-based repair path. Depositing is the most common action and must not sit behind a tab that reads as troubleshooting. |
| **D6** | The bet log — sixth tab, or below the card? | **Below the card**, where it already is and where `orbit-feedback-adjustments.md` put it. It is a record, not a setting; a tab strip of settings should not have a ledger in it. |
| **D7** | Both trio buttons pressable at once (as drawn)? | **No.** The current state renders as *current* — filled, no hover, not activatable — and the others stay live. The mockup shows `RUN` and `STOP` equally pressable under a `● RUNNING` header, which asks the player to work out which state they are already in. |
| **D8** | Component extraction, or tabs in place? | **Extract.** Each tab owns its fields *and* its handlers. The page is 1,379 lines and its script is ~640 of them; leaving that intact is the same trap that took `OrbitModal.vue` from the 916 lines #141 complained about to 1,397. |
| **D9** | The stat strip's third cell | **`WALLET BALANCE`** (mockup 2), not `ROUNDS LEFT` (mockup 1). `roundsLeft` stays available in `useOrbit.ts:762` and is still rendered by `OrbitStatusBanner` in the modal. |

---

## 4. The type scale

The page inherits the modal's **ranking** and shifts every step **up one size class**, because it is
1000px wide rather than 380px. Declared under a page-scoped `.orbit-page` class in `arcade.css`, so
the values shipped in `66e4df3` for the panel are untouched.

| Role | Modal (`66e4df3`) | Page | Token |
| --- | --- | --- | --- |
| Surface title (`ORBIT`) | 14px PS2P | **20px** | `.orbit-page .orbit-page-title` |
| Tab label | — | **11px** PS2P | `.orbit-tab` |
| Section title (`GAME SETUP`) | 9px PS2P / ink-dim | **11px** / ink-dim | `.orbit-page .orbit-section-title` |
| Field label (`WORD (6 CHARACTERS)`) | 7px eyebrow | **9px** PS2P / ink-dim | `.orbit-page .orbit-field-label` |
| Body + helper copy | 19px VT323 | **20px** | `.orbit-page .dialog-copy` |
| Stat value (`5,00`) | — | **32px** VT323 | `.orbit-stat__value` |
| Stat caption (`ORBIT HOLDS`) | 8px / ink-mute | **9px** / ink-mute | `.orbit-stat__label` |
| Field height | 40px | **48px** | `--ctl-h-lg` (exists) |
| Trio button height | — | **56px** | `--ctl-h-xl` (**new**, `colors.css` — see §2) |

⚠️ **Raising the section title back to 11px is not a reversal of the 2026-08-05 ruling.** That call
dropped it from 11px/ink to 9px/ink-dim because *in a 380px panel* three stacked headings outranked
the money numbers and the controls they introduced. `arcade.css` states the principle explicitly:
the rule is **"the RATIO to what a title introduces, not an absolute floor."** Here the controls go
to 48px and the stat values to 32px, so 11px **ink-dim** sits *lower* in the ranking than 9px did in
the panel. The ratio is preserved; only the absolute size moves. `.orbit-card-title` keeps its step
below the section title.

---

## 5. The `/orbit` page

### 5.1 Structure

```
┌ .arcade-frame--orbit ─────────────────────────── max-w-[1000px] ┐
│ ◇ ORBIT    ● RUNNING                                   ◀ Back   │
│ Orbit auto-plays a game for you… How it works ▶                 │
│                                                                 │
│ ┌ stat strip (violet frame) ──────────────────────────────────┐ │
│ │ ORBIT HOLDS 5,00 USDC │ PER ROUND 1,00 USDC │ WALLET 159,84 │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
│ [notice / error band — above the tabs, always visible]          │
│                                                                 │
│ ┌ ① GAME ┬ ② MONEY ┬ ③ STOP RULES ┬ ④ RECOVER ┬ ⑤ TELEGRAM ─┐  │
│ │                                                            │  │
│ │  active panel — joined to its tab by the seam              │  │
│ └────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘

        ▶ RUN ORBIT        ⏸ STOP ORBIT       ⏻ TURN ORBIT OFF
        Start automated    Pause automated    Cash out and leave

┌ bet log — below the card, unchanged ────────────────────────────┐
```

The tab bar sits **inside** the card; the active panel is joined to its tab by the seam, which is
what makes a tab bar read as tabs rather than as a row of buttons. The trio sits **outside and
below** — the owner's *"they dictate everything"* — which also keeps the controls from scrolling
away behind a long Telegram panel.

**Signed out** the trio does not render at all; the existing sign-in pitch replaces the whole
tabs-and-controls block, exactly as it replaces the step list today.

### 5.2 The control trio and the seven states

`useOrbit.ts:774` exposes seven values, so each button needs a defined answer in all of them:

| `orbitStatus` | `▶ RUN ORBIT` | `⏸ STOP ORBIT` | `⏻ TURN ORBIT OFF` |
| --- | --- | --- | --- |
| `signed-out` | *trio hidden — sign-in pitch renders instead* | | |
| `off` | **Start Orbit** (primary) | disabled | disabled — nothing to leave |
| `unfunded` | disabled → `Add money first` | disabled | enabled |
| `running` | **current state** — filled, not activatable | **Pause** | **Cash out and leave** |
| `stopped` | **Resume** | **current state** — filled, not activatable | enabled |
| `paused` (ran dry) | disabled → `Add money first` | disabled — already halted | enabled |
| `cooling-off` | disabled → shows `enableLockedLabel` | disabled | disabled |

**The current state is not a button.** It keeps the filled treatment so the state is legible at a
glance, but loses hover, focus-activation and `@click`, and is marked `aria-disabled` with the state
named in its accessible label.

⚠️ **Why green and red fills here do not reopen the colour salad.** `orbit-page-restructure.md` §4.2
prevents the salad with a **weight** rule — colour lives only in pips, 3px bars, one status tag and
one button, and *every value stays plain ink*. The trio obeys that rule: it is **one** row of
controls, **outside** the card, and it introduces **no** coloured value anywhere. Inside the card a
running, fully-configured Orbit still renders no amber and no red (**AC7**). `TURN ORBIT OFF` is
drawn as an **outline** control rather than a third fill, because it forces a cash-out and starts the
cool-off and must not carry the same visual pull as the reversible pause beside it.

### 5.3 The tabs

| Tab | Contents | Moved from |
| --- | --- | --- |
| **① GAME** | Word field (letter cells, or the preset picker on preset types) · the auto-pick opt-in · game mode select | step 1 |
| **② MONEY** | Add money (integer + confirm) · Cash out (`MinimumCashOut` → full balance, MAX one-tap) | step 2 + the promoted cash-out row |
| **③ STOP RULES** | Auto-cash-out threshold · cash-out floor · each with its own explicit off | step 3 |
| **④ RECOVER** | Signature + amount, to re-credit a deposit that never landed | the recover section |
| **⑤ TELEGRAM** | Link / verify / unlink · the three toggles (cash-out **on**, zero-out **on**, any-win **off**) | step 4 |

Tab state is **local component state**, not a route param — `/orbit` keeps exactly one URL, and
`EMBED_REDIRECT_ROUTES` membership is unchanged. The default tab is `① GAME`, except when
`needsFirstDeposit` or `isPausedForFunds` is true, in which case the page opens on `② MONEY` — the
same "send them where the blocker is" behaviour the step list had by un-collapsing step 2.

### 5.4 Mobile

Five tabs at 11px do not fit 360px. The strip **scrolls horizontally** with scroll-snap, and the
active tab is scrolled into view on change.

Rejected: **wrapping to two rows**, which breaks the seam joining the active tab to its panel — the
one cue that makes the control read as tabs. Also rejected: **collapsing to a `<select>`**, which
hides the map of what Orbit can do behind a control that looks like a form field.

The trio stacks to full width below `sm`, keeping its order (run, stop, off) so the destructive
control stays last.

### 5.5 Errors and notices

The page-level notice/error band sits **above the tab bar and inside the card**, so the result of an
action stays visible when the player moves to another tab. Field-level validation stays **inline**
beside its field, inside the tab that owns it.

This split matters because tabs can hide a result the step list could not: a deposit error raised in
`② MONEY` and rendered only inside `② MONEY` disappears the moment the player opens `③ STOP RULES`
to check something.

---

## 6. Out of scope

- **`ROUNDS TO PLAY`** — see D2. A round budget is a feature card with a migration and an integration
  test, not part of a re-presentation.
- **`OrbitModal.vue`** — redesigned in `66e4df3` and consistent already. This card does not touch it,
  and §2's shared-component rule is what keeps that true.
- **`useOrbit.ts`** — no new state. It is 1,175 lines and worth splitting, but not in a card whose
  diff is already a page rewrite; unrelated refactoring stays out.
- **The console module, the redirect toast, the first-win modal, the notifications** — untouched.
- **Backend** — zero changes (**AC17**).

---

## 7. File checklist

### 7.1 Frontend (`monkify-front`)

| File | Action |
| --- | --- |
| `src/pages/OrbitPage.vue` | **Rewritten** — shell only, target ~250 lines |
| `src/components/orbit/OrbitTabBar.vue` | **New** — numbered strip, scroll-snap, seam |
| `src/components/orbit/OrbitStatStrip.vue` | **New** — holds / per round / wallet |
| `src/components/orbit/OrbitControlBar.vue` | **New** — the trio and the state table in §5.2 |
| `src/components/orbit/tabs/OrbitGameTab.vue` | **New** |
| `src/components/orbit/tabs/OrbitMoneyTab.vue` | **New** |
| `src/components/orbit/tabs/OrbitStopRulesTab.vue` | **New** |
| `src/components/orbit/tabs/OrbitRecoverTab.vue` | **New** |
| `src/components/orbit/tabs/OrbitTelegramTab.vue` | **New** |
| `src/components/orbit/OrbitStepRow.vue` | **Deleted** — page was its only consumer |
| `src/assets/styles/arcade.css` | The `.orbit-page` scale (§4), `.orbit-tab*`, `.orbit-stat*` |
| `src/assets/styles/colors.css` | **New `--ctl-h-xl: 3.5rem`** beside the existing three (§2) |
| `src/components/orbit/OrbitStatusBanner.vue` | **Unchanged** — still the modal's, page stops rendering it |
| `src/components/orbit/OrbitAmountField.vue` | **Unchanged** — consumed as-is by `② MONEY` and `④ RECOVER` |
| `src/components/orbit/OrbitWordField.vue` | **Unchanged** — consumed as-is by `① GAME` |
| `src/components/orbit/OrbitBetLog.vue` | **Unchanged** — still rendered below the card |

### 7.2 Meta (`monkify-meta`)

| File | Action |
| --- | --- |
| `specs/orbit-new-ui.md` | This doc |
| `specs/orbit-page-restructure.md` | Superseding banner added (§0 of this doc) |
| `INDEX.md` | Row for this spec; restructure row marked partially superseded |
| `features/orbit-custodial-wallet.md` | `/orbit` description updated at ship time |

---

## 8. Repos, branches, worktrees

Branch `feature/orbit-new-ui` **already exists and is pushed** in all three repos (cut 2026-08-06,
see §0). Worktrees are at `.worktrees/orbit-ui-overhaul/<repo>` — the directory retains the older
slug because the front worktree carries `node_modules` and live `node` processes, and moving it on
Windows risks a half-completed move. The branch, not the directory name, is authoritative.

`monkify-backend` is on the branch but receives **no work from this card**.

---

## 9. Tests

`monkify-front` has no test framework (no `.spec`/`.test` anywhere in the repo), so the bar is:

1. `yarn lint` — clean. ⚠️ Run it from the worktree with the hub's `yarn.lock` present and
   `core.autocrlf=true`; a mixed-ending file fails prettier with `Insert ␍` and nothing else.
2. `yarn build` — clean.
3. The **seven-state manual pass** in §11. The state table in §5.2 *is* the checklist.

**No backend test run is attributable to this card** — it changes no C#. The suite last ran green on
this branch at `33e396e` (834 passed, 0 failed, via `scripts/run_tests.bat`, which excludes the
`ProductionGames` Monte Carlo suite by design).

---

## 10. Acceptance criteria

1. `/orbit` renders five tabs; exactly one panel is visible; the active tab is joined to its panel by the seam.
2. Every tab is reachable at any time — no tab is gated, collapsed or hidden by another's state.
3. The page opens on `① GAME`, except when `needsFirstDeposit` or `isPausedForFunds` is true, when it opens on `② MONEY`.
4. The stat strip shows `ORBIT HOLDS`, `PER ROUND` and `WALLET BALANCE` with live values.
5. The trio renders below the card and behaves exactly as the §5.2 table specifies in **all seven** states.
6. The current state renders filled, is not activatable, and names its state to a screen reader.
7. A **running, fully-configured** Orbit renders **no amber and no red inside the card**; every value in the card is plain ink.
8. `TURN ORBIT OFF` is an outline control, visually subordinate to `STOP ORBIT`.
9. `② MONEY` holds **both** add-money and cash-out. `④ RECOVER` holds only the signature repair path.
10. The bet log renders below the card and still loads only when shown.
11. `OrbitStepRow.vue` is deleted and has no remaining imports.
12. The **files** `OrbitStatusBanner.vue`, `OrbitAmountField.vue` and `OrbitWordField.vue` are byte-identical to `66e4df3`, and `OrbitModal.vue` renders identically before and after this card. ⚠️ Their **rendering inside `.orbit-page` is expected to differ** — they inherit the page scale in §4 by design. The requirement is that no *file* changes and the *modal* is unaffected; it is not that the page renders them at panel size.
13. `--ctl-h-xl` is declared in `colors.css`, the control-sizing scale is documented as four steps, and no control introduced by this card sets a literal height.
14. `OrbitPage.vue` is under 350 lines; no new file exceeds 300.
15. At 360px the tab strip scrolls horizontally, the active tab is scrolled into view, and the trio stacks full-width in run/stop/off order.
16. A notice raised by an action in one tab is still visible after switching tabs.
17. `monkify-backend` is diffed against `33e396e` and has **zero** changes from this card.
18. `feature/orbit-ui-overhaul` is merged to `main` **before** this branch (§0 merge-order hazard).
19. `yarn lint` and `yarn build` are clean.

---

## 11. Manual QA (local Docker stack)

Backend on `:6001`, front dev server from the worktree on a **verified-free** port (another
worktree's server commonly holds 8081; a collision fails quietly and you end up testing the wrong
build — grep the served bundle for `OrbitTabBar` to confirm). Free-bets operator via
`POST /dev/launch-token` + `#launch_token=`.

1. **Signed out** → sign-in pitch, no tabs, no trio.
2. **`off`** → `RUN` primary; `STOP` and `TURN OFF` disabled.
3. **`unfunded`** → page opens on `② MONEY`; `RUN` disabled reading `Add money first`.
4. Deposit → **`running`**; `RUN` is filled and inert, `STOP` and `TURN OFF` live.
5. `STOP` → **`stopped`**; `STOP` inert, `RUN` reads `Resume`. Confirm the planner skips and **nothing else changes** — balance intact, no cool-off, no forced cash-out.
6. Let the balance fall below the stake → **`paused`**; `RUN` disabled, `TURN OFF` still live.
7. `TURN OFF` → forced cash-out, then **`cooling-off`**; all three disabled and `enableLockedLabel` shown.
8. Raise a deposit error in `② MONEY`, switch to `③ STOP RULES` → the notice is still visible.
9. At 360px: strip scrolls, active tab scrolls into view, trio stacks.
10. Open the **modal** on `/game/play` and confirm it is unchanged from `66e4df3`.

---

## 12. Revision log

| Date | Change |
| --- | --- |
| 2026-08-06 | Written. Supersedes `orbit-page-restructure.md` §5 (the step list) and its cash-out/recover placement — see §0 and D5. Drops the mockups' round budget (D2) and close/ESC chrome (D3). |
