# Orbit modal improvements — the panel and the page share one control

**Shipped** 2026-08-10 · Trello [#163](https://trello.com/c/77laekQh) ·
branch `feature/orbit-modal-improvements` · spec [`specs/orbit-modal-improvements.md`](../specs/orbit-modal-improvements.md)
Repos: **`monkify-front`** + **`monkify-meta`**. **`monkify-backend` NOT affected** — no entity,
handler, consumer, migration, endpoint or SignalR contract. Every value rendered here was already on
`GET /orbit`, and the cash-out uses the shipped signed `POST` path unchanged.

> ⚠️ **Read [`specs/orbit-modal-improvements.md`](../specs/orbit-modal-improvements.md) §17 before its
> §3.** The spec's change ③ was built and rejected on review; §17 is the authoritative record of what
> actually shipped, and §1/§3/§7.3/§10.2/§12 are marked superseded in place.

## Why

The single-control merge (`orbit-single-control.md`, 2026-08-07) collapsed the **`/orbit` page** to
ONE `RUN`/`RESUME` ↔ `PAUSE ORBIT` toggle plus a neutral `CASH OUT` (`OrbitControlBar`). It left the
**panel** (`OrbitModal.vue`) on the older **transport pair** — `RUN ORBIT` + `STOP ORBIT` both always
drawn, the one matching the current state disabled — and with **no cash-out at all**, so getting money
out of a live round meant leaving the round for `/orbit`.

## What the system does now

### One control component, two surfaces

`OrbitControlBar` renders on the `/orbit` page **and** inside the panel (`compact`). The
RUN/PAUSE/CASH-OUT state table, its labels and its colours exist in **exactly one place**.

⚠️ **Never re-create that table.** A second copy is precisely how the status tag and the panel's
buttons came to disagree before this card.

Two behaviour changes fell out of the swap, deliberately:

- The panel's PAUSE used to be **dead while `unfunded`** (`canStop` gated on funds). The bar's toggle
  is live whenever `status === 'running'` and shows a disabled `RUN ORBIT / Add money first` in
  `unfunded`. **The bar's table wins.**
- The panel's RUN **never said RESUME**. The bar says `RESUME` once Orbit has placed a bet.

The panel keeps one thing the page does not: its toggle **doubles as the enrolment press** for the
round it was opened beside, so `onToggle` spreads `gameParametersId` when Orbit holds none. The page
sends a bare `{ enabled }` because a player standing on `/orbit` has already picked a game. ⚠️ Dropping
that spread breaks "open the panel beside a round, press RUN".

`OrbitControlBar` gained a **`selfExcluded`** blocker, first in `setupBlocker` (above the three setup
steps — no press clears it), sub-note `Auto-play unavailable`, bound on **both** surfaces. It gates
only the RUN direction: ⚠️ **pausing must never have a precondition**, including for a self-excluded
wallet whose Orbit is still running. This also closed the same hole on `/orbit`, where the toggle had
always been live for such a wallet with only the server refusing.

### Money-out from the round screen

The panel's `CASH OUT` sends the **full balance** straight to the wallet signature — no intermediate
confirm row, because the signature **is** the confirmation and a second press would make the panel's
cash-out heavier than the page's for no new protection.

⚠️ **It is never word-gated.** `ADD MONEY` keeps its `hasPlayableChoice` gate; `CASH OUT` sits outside
it. A player who funded Orbit and then could not save a word must still be able to get their money
back. **Money-out is never harder to reach than money-in** (`orbit-ui-overhaul.md`).

⚠️ **`useOrbit.cashOut` RETURNS rather than throws when a mobile deeplink hops out to the wallet app**
(`signOrbitMessage` → `'hopped'`). An unconditional success toast is therefore a lie on that path. The
panel reads the balance **before** the call and toasts only if it actually dropped. `OrbitPage` has the
same exposure and is unchanged — `cashOut`'s signature is shared with the MONEY tab.

Emptying the balance also parks Orbit by consequence (the auto-park watcher), so "cash out and leave"
stays one press with no special code path.

### The stop rules stay in the panel

⚠️ **The panel's `AUTO CASHOUT (PAUSES ORBIT)` editors are UNCHANGED — byte-identical to what shipped
before this card.** Replacing them with two links out to `/orbit` was built, shown to the owner and
**rejected**. Consequences worth stating because a future reader will look for them:

- **`orbit-ui-overhaul.md` §3.1 is NOT re-opened.** It was closed by making the rules editable here,
  and it stays closed.
- **`src/constants/icons.ts` is untouched.** No `notify` glyph shipped; the registry still carries
  exactly two baked fills (`win-at`, `falls-to`) and there must not be a third.

### Rule auto-save — on both surfaces

Both rule surfaces committed on `@blur` / `@keyup.enter` **only**, so a value typed and never blurred
was silently thrown away. Both now also auto-save:

| Surface | Debounce | Flush |
| --- | --- | --- |
| `OrbitStopRulesTab.vue` (`/orbit`) | 1.2 s, armed from `@input` | `onBeforeUnmount` — the tab really does unmount on every tab switch (`OrbitPage` mounts only the active tab) |
| `OrbitModal.vue` (the panel) | 1.2 s, armed from `@input` | **`watch(() => props.open)` going false** |

⚠️ **The panel's flush is deliberately NOT `onBeforeUnmount`.** `OrbitModal` is `v-if`-ed on a feature
flag rather than on `open`, so it stays mounted for the whole session and its unmount hook effectively
never runs — the same trap the file's wallet watcher documents for the word auto-save. `open` going
false **is** the close, and Esc / the backdrop / the ✗ all reach it without ever firing blur.

⚠️ **1.2 s, not the word field's 600 ms.** Every prefix of a number is itself a legal value and
`CashOutAtThreshold` fires at *balance ≥ it*: a pause while typing `100` can briefly save `1`, and the
next win credit then cashes out **for real** and pauses Orbit — money moved, cool-off gap consumed.
`minimumCashOut` is the only other guard. The longer window is the mitigation; the residual risk is
accepted.

⚠️ **Armed from `@input`, NEVER from a `watch` on the input refs.** `commitRule`'s `catch` calls
`syncRuleFields()`, which writes those refs; a watcher would re-arm off that programmatic write,
re-commit, be refused again, and sit in a **PATCH loop**. A DOM `input` event cannot fire from a
programmatic write at all, so the loop is impossible by construction rather than by a guard flag.

Other invariants: one timer **per field** (editing the floor must not cancel a pending threshold
save); `commitRule` cancels its own field's timer as its first statement, so blur/Enter/flush each
send **exactly one** PATCH; and in the panel a **wallet change cancels** both timers rather than
flushing them — a pending floor belongs to the wallet that typed it.

The **word** fields already debounced at 600 ms on both surfaces and are **left alone**. The card's
"word" complaint was a false alarm, verified during refinement.

### `/orbit?tab=` and the Telegram entry point

`/orbit` accepts a **validated `?tab=<id>`**, matched against the **visible** tab list (`tabs`, not
`TABS`) and falling back to `game`. An explicit valid tab **beats** the "open on MONEY when
unfunded/first-deposit" default.

⚠️ **Validation runs against loaded state, not at `setup()`.** `tabs` drops `telegram` while `orbit`
is still `null`, so a synchronous-only check would silently downgrade a valid `?tab=telegram` deep
link to `game` on any cold load — including the signed-out one, where the target must survive the
sign-in. It is folded into `OrbitPage`'s **existing** two-branch pattern (sync check + self-stopping
`watch(orbit, …)`) rather than added as a third mechanism, so it inherits that code's documented TDZ
sidestep.

`activeTab` stays a local ref otherwise: **no URL write-back on tab clicks**, so a player who deep-links
to `?tab=telegram` and then clicks GAME leaves a stale query. Accepted — the query is read once, on
arrival.

Its caller is the panel's way out, which is also the card's Telegram hook:

| `telegramEnabled` | Label | Target |
| --- | --- | --- |
| true | `Set up notifications and more in Orbit settings` | `/orbit?tab=telegram` |
| false | `More in Orbit settings` | `/orbit` |

⚠️ **Both halves flip together.** Naming notifications on a deployment whose `/orbit` renders no
TELEGRAM tab is a promise the page cannot keep — the same "no control that can only ever fail" rule
that drops the tab in the first place. Telegram **linking** is still page-only; the panel only points
at it.

## Key code touchpoints

- `src/components/orbit/OrbitControlBar.vue` — `compact` + `selfExcluded` props; `setupBlocker`'s new
  first branch; icon 28 → 24 in compact.
- `src/assets/styles/arcade.css` — ⚠️ **`.orbit-trio*` is NOT scoped under `.orbit-page`.** The
  component declares no styles of its own, so these rules *are* the component, and the panel has no
  `.orbit-page` ancestor — scoped, the bar rendered as three unstyled inline buttons there. De-scoping
  covers the main block **and both `:focus-visible` lists** (base outline + Orbit's violet
  `outline-color` override), or the panel focuses with the browser default. **Never** re-scope them,
  copy them into the modal, or put `.orbit-page` on a dialog (it carries the page's `relative z-[2]`
  starfield fix and its layout). `.orbit-trio--compact` is the panel size; a
  `prefers-reduced-motion` block lives beside them, which the page instance had been missing.
- `src/components/orbit/OrbitModal.vue` — `OrbitControlBar` mount, `onToggle`, `onCashOut`, the rule
  debounce + close-flush, the new way-out copy and target. `runTarget` and `hasFundsForRound` are
  **kept** (the bar cannot derive them — only the panel knows the round on screen is a valid target
  for a first, enrolling press).
- `src/pages/OrbitPage.vue` — `requestedTab` + `applyInitialTab()`, and the `self-excluded` binding.
- `src/components/orbit/tabs/OrbitStopRulesTab.vue` — the debounce and unmount flush.
- **Not changed:** `src/types/orbit.ts`, `src/services/*`, `src/hooks/useOrbit.ts` (no new export, no
  signature change — the panel simply started destructuring `cashOut` and `nextManualCashOutLabel`),
  `src/constants/icons.ts`, `src/routes.ts`, `src/main.ts`.

## Verification

**No tests.** `monkify-front` has no test runner (no `.spec`/`.test` files anywhere), and with **zero
backend surface** `AGENTS.md` rule 8's mandatory happy-path integration test has nothing to exercise —
this is a frontend-only minor change. The bar was `yarn lint` + `yarn build` clean, plus owner QA on
the local Docker stack with `VUE_APP_ORBIT_ENABLED=true`, re-run after merging `origin/main` forward
(card #162 shipped mid-flight).

⚠️ **`telegramEnabled` is false on a default local stack**, so the notifications half of the way-out
link needs a placeholder bot token supplied through the compose `--env-file` hook that
`monkify-backend/docker-compose.yml` already documents, from a file **outside every repo**. Note that
`OrbitSettings.IsTelegramConfigured` treats a `MOCK_`-prefixed token as **absent** by design (committed
config must fail closed into an inert notifier, never a live bot), so the placeholder cannot use that
prefix — and every real Bot API call therefore fails. Good enough to render the surface; not a working
link.
