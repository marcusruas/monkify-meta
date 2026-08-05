# Home Hero Sprite + Wallet-Connected Animation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the home hero's borrowed `guide/game-modes.gif` with the purpose-drawn two-layer art (a static coin + one of three ship GIFs) and drive the ship from the player's wallet state — sleep / alert / orbit.

**Architecture:** One component (`HomeHero.vue`) gains a three-state machine. The art column becomes two absolutely-registered layers on one shared 740×500 canvas: a static `coin.png` in flow (the sizing layer, carrying the glow) and one ship `<img>` absolutely positioned over it whose `src` swaps with the state. The state machine is gated on a mount-time *capability check* — can an automatic connect even happen in this page session? — so the 1.2 s alert fires only on a connect the player performed, never as boot furniture. Three orphaned assets are deleted and one stale stylesheet comment is rewritten.

**Tech Stack:** Vue 3.2 Composition API + TypeScript 5.5, Vue CLI 5 (webpack), Tailwind 3, `solana-wallets-vue`. No test runner in `monkify-front` — the gates are `yarn lint`, `yarn build`, and measured browser QA.

## Global Constraints

- **Source of truth:** `monkify-meta/specs/home-hero-sprite.md` (on this branch). Where it and the Trello card disagree on a measured number, **the spec's §4 wins**.
- **Worktrees only:** work in `.worktrees/home-hero-sprite/monkify-front` and `.worktrees/home-hero-sprite/monkify-meta`, branch `feature/home-hero-sprite`. Both hubs stay on `main`. **Never** `git checkout`/`switch` in a hub.
- **`monkify-backend` is untouched** — no branch, no worktree, zero diff against `main` (AC21).
- **CRLF is mandatory.** `monkify-front` is checked out with `core.autocrlf=true`. After editing any file there, run `unix2dos` on it before `yarn lint`, or prettier reports `Insert ␍` on every line. **Never `sed -i`** in this repo.
- **No unit tests, no integration tests.** Level is *Minor change, frontend-only* (`constraints.md` §3 — no entity, migration, handler, consumer, endpoint, DTO or SignalR change; `monkify-front` has no test runner). Nothing in `SpaceStroker.Tests` or `SpaceStroker.Tests.Integration` is touched.
- **`html.light-graphics` rules** hold in the band: no `backdrop-filter`, no `blur()`, no `mix-blend-mode`, no infinite **CSS** animation. The GIF is the scoped exception (spec decision 6) and it keeps animating — **do not add a `usePerfMode` branch to this file**.
- **#138's shipped ACs are regressions if broken:** the art is `shrink-0` and sized by **height, never a width cap**; the band is **frameless**; the art is **not rendered below 640 px**; the band is **≤ 265 px at 1366×768 in both states**; the art stays **pixel-art** (`pixelated`).
- **Exact literals:** `ALERT_MS = 1200` (the asset's measured total, spec §4.2), `AUTO_CONNECT_GRACE_MS = 2500` (matched to `App.vue:99`'s own deadline), `ART_QUERY = '(min-width: 640px)'`, art height `h-[138px] md:h-[176px]`.
- **Assets are already committed** on this branch at `src/assets/hero/` (commit `6a3705a`) — do not re-source or re-encode them.

### Amendment to the spec — resolved with the owner 2026-08-05

Spec §7.3.1 initialises `shipState` from `props.connected` alone. That covers the **deeplink** path (`hydrate()` is synchronous, `useMobileWallet.ts:151`) but **not** `solana-wallets-vue`'s `autoConnect` (`main.ts:132`), whose `adapter.connect()` resolves **0.3–1 s after boot**. Implemented verbatim, a returning player would watch a sleeping ship for that second — exactly the boot furniture **AC3** forbids and QA step 4 tests for.

**Owner's decision: honour AC3.** A persisted `walletName` opens the hero **in orbit immediately**, and a boot fallback cuts to sleep if that connect never lands (locked or rejected wallet). `isMobileBrowser()` deliberately does **not** arm the optimistic open — only the alert suppression — because a plain tablet browser with no persisted wallet has no injected wallet to connect to, so opening it in orbit would be a lie the fallback then has to take back.

### Second amendment — owner's direct visual override, 2026-08-05

After seeing the band rendered, the owner asked for a **taller section, bigger and less edge-hugging art, and a small wobble on the whole composite**. This knowingly overrides three of the card's own ACs. Measured results, all re-verified in the browser:

| | Spec / AC | Shipped | AC status |
| --- | --- | --- | --- |
| Art height at `md` | ≤ 178 px (AC13) | **200 px** | **AC13 amended** — the coin floor (≥ 153 px) still holds at **176 px** |
| Art height at `sm` | 138 px | **156 px** (coin 137.3 px) | — |
| Band at 1366×768 | ≤ 265 px (AC12) | **288 px**, cold *and* connected | **AC12 amended** — the art now drives **both** states, so the two are identical |
| Band padding | `pt-6 pb-10 md:pb-12` | `pt-8 pb-12 md:pb-14` | — |
| Right gutter | flush right | `md:mr-8 lg:mr-16` (64 px at 1366) | — |
| Infinite CSS animation in the band | banned (AC15 / §7.10) | `hero-art-drift`, 5 s, transform-only | **AC15 amended, but honoured where it matters** — killed under `html.light-graphics` |

The wobble is **transform-only** (`translate3d` + `rotate`, ±6 px / ±0.6°), so the compositor runs it with no layout, no repaint, and **no per-frame re-derivation of the coin's drop-shadow** — which is what decision 9 put that glow on the static layer to avoid. `html.light-graphics .hero-art-wobble { animation: none }` drops it entirely on CPU-rasterizing devices, so §7.10's actual target is unaffected; unlike the ship GIF (an argued exception — it *is* the wallet feedback), this drift is pure decoration and costs nothing to lose there.

**AC14 re-verified after the resize:** at 640 px the headline is still exactly **2 lines** (copy column 357.1 px vs the ~266 px the headline needs).

### Fourth amendment — SUPERSEDES the third, 2026-08-05

The double-play was **reverted at the owner's instruction** — the alert plays **once** per connect (`playAlert()`, no count). Two things came out of it and both stayed:

**1. The `:key` remount alone does NOT replay this GIF — this was a real latent bug.** The owner reported the "double" play still looked single. Cause: webpack **inlines `ship-alert.gif` as a data URI** (5 689 B, under the 8 KB threshold), and Chrome keys its decoded-image cache by **URL**, so a freshly-mounted `<img>` pointing at the identical URI is handed back the *same finished animation* and renders the frozen last frame. The spec's decision 12 assumed a remount was sufficient; it is not.

**Fix:** `shipSrc` returns `` `${shipAlertArt}#${alertRun.value}` ``. A fragment changes the URL string without touching the payload, forcing a genuine re-decode from frame 0. Verified — pass 1 `ALERT#1`, pass 2 `ALERT#2`, distinct URLs *and* distinct nodes (previously: distinct nodes, **identical URL**). Works for the inlined data URI and for a plain emitted file, so it survives the asset crossing the inline threshold in either direction.

⚠️ **This is really an AC5 fix.** Connect→disconnect→connect uses the same mechanism, so AC5 ("the second connect must NOT render a frozen last frame") was going to fail on ship. It failed silently and looked like a broken wallet, exactly as the spec warned.

**2. The alert now fires on an automatic connect ONCE PER TAB** (owner's choice). Suppressing it unconditionally (AC3) made the reaction unreachable for returning players — their wallet reconnects on every load, so the only way to see it was an explicit disconnect→connect. Gated on `sessionStorage['ss.heroAlertSeenThisTab']`, which dies with the tab, so it is a per-visit reaction and never becomes furniture on every route change. `try/catch` on both read and write (private-mode Safari throws); a read failure degrades to "already seen". **AC3 is amended** to "no *sleeping ship* during boot, and at most one alert per tab".

### Third amendment — the alert plays TWICE, 2026-08-05 (SUPERSEDED — see above)

One pass is 1200 ms of which **1000 ms is the neutral-pose hold**, so the actual reaction (the `!` popping in frames 0–1) lasts **200 ms** and the owner could not see it at the new art size. `ALERT_PLAYS = 2`.

⚠️ **This is not a loop count.** `ship-alert.gif` has no NETSCAPE application block (§4.2), so it cannot be told to repeat — a second pass is a **remount**. `playAlert(playsLeft)` bumps `alertRun` (changing the `<img>`'s `:key`) on every pass, so Vue destroys and recreates the element and a fresh `HTMLImageElement` decodes from frame 0. This is the same mechanism AC5's connect→disconnect→connect case already required.

The seam is free in both directions: each pass ends on the 1000 ms neutral hold and the next opens on the `!`, so the repeat reads as a deliberate double-blink; the final pass still ends on the neutral pose where `ship-orbit` re-enters.

**Measured in the live component** (calling the real `playAlert` on the mounted instance):

| t | state | `alertRun` | node remounted |
| --- | --- | --- | --- |
| 9 ms | `alert` | 1 | yes — pass 1 from frame 0 |
| 1213 ms | `alert` | **2** | **yes — pass 2 genuinely replays** |
| 2405 ms | `orbit` | 2 | yes — handoff |

**Single-flight preserved:** only one `alertTimer` is ever live, so `clearAlertTimer()` cancels the whole remaining chain. Verified by cancelling mid-**pass 1** and mid-**pass 2** — the ship went to `sleep` and never reached `orbit` in either case. This matters because the chain **doubles** the window in which a disconnect can interrupt (AC6/AC8), and AC2's timing becomes **~2400 ms**, not 1200 ms.

---

## File Structure

| File | Responsibility after this plan |
| --- | --- |
| `src/components/home/HomeHero.vue` | **The whole feature.** The two-layer art markup, the three-state machine, the capability check, both timers and their cleanup, the 640 px gate, the preload. Its header comment and headline-measure table describe the *new* art. |
| `src/assets/hero/{coin.png,ship-sleep.gif,ship-alert.gif,ship-orbit.gif}` | The delivered art. **Already committed — not touched by this plan.** |
| `src/assets/styles/arcade.css` (≈1106–1111) | Comment only. Records *why* `.hero-art` is excluded from the light-graphics `filter: none` kill, now that the glow sits on a static coin layer. **Selector list unchanged.** |
| `src/assets/{edson-ship.gif,edson-ship.png,coin.png}` | **Deleted.** All three verified unreferenced. |
| `src/pages/HomePage.vue`, `src/utils/wallet.ts`, `src/utils/walletDeepLink.ts` | **Expected: no change.** Verified — `connected` (`HomePage.vue:36`), `WALLET_NAME_STORAGE_KEY` (`wallet.ts:16`) and `isMobileBrowser()` (`walletDeepLink.ts:13`) already exist and are already exported. Read, never written. |

`monkify-meta` docs (`features/home-dashboard.md`, `specs/home-hero-trim.md`, `specs/light-graphics-mode.md`, `INDEX.md`) are **ship-time** work — `/monkify-ship-feature`, not this plan.

---

## Task 1: The art layer and the wallet state machine

**Files:**
- Modify: `src/components/home/HomeHero.vue` (script, template, scoped style, and both comment blocks)
- Test: none — `monkify-front` has no test runner. Gates are `yarn lint`, `yarn build`, and the browser checks in Task 3.

**Interfaces:**
- Consumes: `props.connected: boolean` (already declared, `HomeHero.vue:71`); `WALLET_NAME_STORAGE_KEY: string` from `@/utils/wallet`; `isMobileBrowser(): boolean` from `@/utils/walletDeepLink`.
- Produces: nothing exported. `shipState`, `alertRun`, `shipSrc`, `shipAlt` are component-local. No change to the three emits (`practice`, `history`, `how-to-play`) or to the `connected` prop's contract.

- [ ] **Step 1: Confirm the file's line endings before touching it**

```bash
cd C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front
od -c src/components/home/HomeHero.vue | head -3
```

Expected: `\r \n` pairs. If the file is CRLF (it is), every edit must be followed by `unix2dos` in Step 8.

- [ ] **Step 2: Swap the imports**

Replace the `vue` import and delete the mode-sprite import (`HomeHero.vue:2` and `:11`). The four asset imports and the two wallet-utility imports go alongside them.

```ts
  import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
  import WalletButtons from '@/components/header/WalletButtons.vue';
  import { useMobileWallet } from '@/hooks/useMobileWallet';
  import { usePlayerPreferences } from '@/hooks/usePlayerPreferences';
  import { usePlayerStats } from '@/hooks/usePlayerStats';
  import { useLiveGames } from '@/hooks/useLiveGames';
  import { useGame } from '@/hooks/useGame';
  import { useGamePicker } from '@/hooks/useGamePicker';
  import { GameStatus } from '@/types';
  import { WALLET_NAME_STORAGE_KEY } from '@/utils/wallet';
  import { isMobileBrowser } from '@/utils/walletDeepLink';
  import coinArt from '@/assets/hero/coin.png';
  import shipSleepArt from '@/assets/hero/ship-sleep.gif';
  import shipAlertArt from '@/assets/hero/ship-alert.gif';
  import shipOrbitArt from '@/assets/hero/ship-orbit.gif';
```

⚠️ `import modeSprite from '@/assets/guide/game-modes.gif';` **must be gone** — `yarn lint` fails it under `no-unused-vars` otherwise.

- [ ] **Step 3: Rewrite the file's header comment**

The existing block (`HomeHero.vue:13–70`) documents the temp sprite, its provenance from `/how-to-play`, the "swap back is a two-line revert" framing and the §7.10 exception as scoped to a 45 KB borrowed asset. **Every one of those is false after this card.** Replace **only the `ART —` paragraph** (currently lines 25–35); the `COPY`, `BUTTONS` and `COPY RULE` paragraphs are #138's and stay verbatim.

```ts
  // WHAT IT IS NOW, and what each rule is protecting against:
  //   ART — PURPOSE-DRAWN, and TWO LAYERS on one shared 740x500 canvas: a static `hero/coin.png` and
  //   ONE ship <img> over it whose src swaps between sleep / alert / orbit. 27 KB total, replacing the
  //   45 KB `guide/game-modes.gif` this band BORROWED from /how-to-play's PICK A MODE — the front door
  //   and the guide were showing the same picture. Still sized by HEIGHT and `shrink-0`, so the art's
  //   height is a constant; but at 176px it is now TALLER than the connected copy column (151px), so it
  //   DOES drive the band in that state. That is measured and inside AC2 (248px < 265px) — it is not
  //   licence to grow it. Below `sm` it is not rendered at all, and the state machine allocates nothing
  //   there: beside the copy at 390px it leaves the headline a ~217px column and breaks it to three
  //   lines, and stacking it instead adds back the height #138 exists to remove.
  //   ⚠️ THE COIN IS LOAD-BEARING, NOT DECORATION. `ship-orbit.gif`'s 7th frame (the 700ms one) is
  //   EMPTY — the artist drew "behind the coin" by hiding the ship. Every frame uses disposal 2
  //   (restore to transparent), so the coin below shows through. Lose the coin layer and that frame
  //   reads as the ship vanishing.
  //   ⚠️ NO `usePerfMode` SWAP. The ship GIF loops on every device, `html.light-graphics` included,
  //   which specs/light-graphics-mode.md §7.10 bans infinite animation under. A SCOPED EXCEPTION,
  //   EXTENDED from #138's and argued, not overlooked: 27 KB against the 45 KB precedent, and because
  //   the coin is a static PNG exactly ONE GIF is ever decoding — the same count as before. Only the
  //   file changed. The exposed range is narrow (640-1023px, or a desktop with hw accel off).
  //   ⚠️ THE GLOW IS ON THE COIN, NOT THE WRAPPER. A static layer means the browser computes the
  //   drop-shadow once instead of re-deriving it on every GIF frame — on exactly the CPU-rasterizing
  //   devices light-graphics exists for. The ship carries no filter and flies clean over it.
```

- [ ] **Step 4: Add the state machine, below the `defineEmits` block**

Insert after the `const { chooseGame } = useGamePicker();` line (`HomeHero.vue:88`), before `shortAddress`.

```ts
  // ── THE SHIP'S STATE MACHINE ───────────────────────────────────────────────────────────────────
  // `props.connected` is `!!effectivePublicKey` (HomePage.vue:36), and it goes false->true in FOUR
  // situations, only ONE of which is a player reacting to this page:
  //   A the player clicks Connect (desktop extension picker)      -> ALERT
  //   B `solana-wallets-vue` auto-connects from the persisted `walletName` (main.ts:132), ~0.3-1s
  //     AFTER boot, because `adapter.connect()` is async                      -> straight to ORBIT
  //   C App.vue:97 `autoConnectInWalletBrowser()` inside a wallet's in-app browser, within its
  //     2500ms deadline                                                        -> straight to ORBIT
  //   D a mobile deeplink session restored by `hydrate()` (useMobileWallet.ts:151) — SYNCHRONOUS,
  //     before first render, so it is never even a flip                        -> mounts in ORBIT
  // A bare `watch(connected)` cannot tell A from B or C, and would hand a returning player a 1.2s
  // alert as page-load furniture. Hence the capability check below.
  type ShipState = 'sleep' | 'alert' | 'orbit';

  const ALERT_MS = 1200; // ship-alert.gif's own measured total (100 + 100 + 1000)
  const AUTO_CONNECT_GRACE_MS = 2500; // App.vue:99's deadline — the slowest automatic path in the app
  const ART_QUERY = '(min-width: 640px)'; // the `sm` breakpoint the art wrapper uses

  // Is a persisted wallet selection sitting in localStorage? `pruneStoredWalletName()` (main.ts:142)
  // has already dropped it if it pointed at a blocked wallet, so a stale MetaMask pick cannot arm
  // anything here. READ ONLY — this file never writes the key.
  // ⚠️ try/catch like every other localStorage read in this codebase: private-mode Safari THROWS.
  // A throw degrades to "an auto-connect may be coming" (suppress the alert) — never to a crash.
  const hasPersistedWallet = (() => {
    try {
      return !!localStorage.getItem(WALLET_NAME_STORAGE_KEY);
    } catch {
      return true;
    }
  })();

  // Can an automatic connect happen AT ALL in this page session? Situation B needs the persisted
  // selection; situation C only ever runs on a mobile UA. If NEITHER is true — the dominant case, a
  // desktop cold visit — the next false->true is PROVABLY the player's own click, so the alert fires
  // with no timing guess whatsoever. The window only exists where an auto-connect is really on the
  // table, which is what makes this a capability check rather than a grace window.
  const autoConnectPossible = hasPersistedWallet || isMobileBrowser();
  let suppressAlertUntil = autoConnectPossible ? Date.now() + AUTO_CONNECT_GRACE_MS : 0;

  // ⚠️ WHY `hasPersistedWallet` ALSO DECIDES THE OPENING FRAME. `autoConnect` is ASYNC: publicKey
  // lands 0.3-1s after boot, so `props.connected` is still FALSE here at setup for situation B.
  // Initialising from `props.connected` alone (as the spec's §7.3.1 snippet does) would show a
  // returning player a SLEEPING ship for that second — precisely the boot furniture AC3 forbids.
  // So a persisted wallet opens in orbit and `armBootFallback` below takes it back if the connect
  // never lands. `isMobileBrowser()` deliberately does NOT arm this — only the alert suppression —
  // because a plain tablet browser with no persisted wallet has no injected wallet to connect to,
  // and opening THAT in orbit is a lie the fallback then has to visibly retract.
  const shipState = ref<ShipState>(props.connected || hasPersistedWallet ? 'orbit' : 'sleep');

  // ⚠️ LOAD-BEARING. `ship-alert.gif` carries NO NETSCAPE application block at all, so every browser
  // plays it exactly once and freezes on its last frame. Pointing `src` back at an already-played URL
  // renders that frozen frame — the second connect would silently do nothing and look like a broken
  // WALLET, not broken art. Bumping this forces the <img> to remount, and a fresh HTMLImageElement
  // restarts GIF decode from frame 0. Keying on `shipState` alone does NOT work: the string is
  // unchanged from the previous alert.
  const alertRun = ref(0);

  let alertTimer: number | null = null;
  let bootTimer: number | null = null;

  const clearAlertTimer = () => {
    if (alertTimer === null) return;
    window.clearTimeout(alertTimer);
    alertTimer = null;
  };

  const clearBootTimer = () => {
    if (bootTimer === null) return;
    window.clearTimeout(bootTimer);
    bootTimer = null;
  };

  // The art is not rendered below 640px, so nothing may be scheduled or fetched there. `useIsMobile`
  // is NOT usable for this — its query is `max-width: 1023px`, the light-graphics boundary, not the
  // art boundary. Reactive because a tablet rotating portrait->landscape crosses 640px.
  const canMatch = typeof window !== 'undefined' && typeof window.matchMedia === 'function';
  const artRendered = ref(canMatch ? window.matchMedia(ART_QUERY).matches : false);
  let artQuery: MediaQueryList | null = null;
  let artWorkStarted = false;

  // 27 KB total, so eager preload is trivially cheap — and without it the first src swap shows a gap
  // where the ship should be. `ship-sleep.gif` needs none: it is the initial src in the disconnected
  // state, and the connected-at-boot state opens on ship-orbit, which this warms anyway.
  const startArtWork = () => {
    if (artWorkStarted) return;
    artWorkStarted = true;

    [shipAlertArt, shipOrbitArt].forEach((src) => {
      const warm = new Image();
      warm.src = src;
    });

    armBootFallback();
  };

  // The other half of the optimistic open above: if that auto-connect does not become real — wallet
  // locked, user rejected, extension asleep — cut to sleep when the same window that suppresses the
  // alert closes. Without this the ship orbits forever on a wallet that never connected.
  const armBootFallback = () => {
    if (!hasPersistedWallet || props.connected) return;
    bootTimer = window.setTimeout(() => {
      bootTimer = null;
      if (!props.connected) shipState.value = 'sleep';
    }, AUTO_CONNECT_GRACE_MS);
  };

  const onArtQueryChange = () => {
    artRendered.value = !!artQuery?.matches;
    if (artRendered.value) startArtWork();
  };

  watch(
    () => props.connected,
    (isConnected) => {
      // Every transition clears BOTH timers first. Two overlapping timers is the one way the ship
      // ends up orbiting while the wallet is gone.
      clearAlertTimer();
      clearBootTimer();

      if (!isConnected) {
        shipState.value = 'sleep'; // a cut, not a reverse animation — none was drawn
        // An auto-connect can only ever fire during boot, so once the player has disconnected every
        // later connect in this page session is deliberate BY CONSTRUCTION. This is what makes the
        // connect -> disconnect -> connect case exact rather than timing-dependent.
        suppressAlertUntil = 0;
        return;
      }

      // Below 640px: track the wallet, but schedule nothing. The alert is a 1.2s reaction to a
      // picture nobody is looking at.
      if (!artRendered.value || Date.now() < suppressAlertUntil) {
        shipState.value = 'orbit';
        return;
      }

      alertRun.value += 1;
      shipState.value = 'alert';
      // ALERT_MS is the asset's exact duration, and that matters: the alert's final 1000ms frame
      // returns the ship to the SAME neutral pose orbit re-enters on, so the handoff lands on the
      // seam and there is no visible jump.
      alertTimer = window.setTimeout(() => {
        alertTimer = null;
        shipState.value = 'orbit';
      }, ALERT_MS);
    }
  );

  onMounted(() => {
    if (!canMatch) return;
    artQuery = window.matchMedia(ART_QUERY);
    artRendered.value = artQuery.matches;
    // Safari < 14 only has the deprecated addListener API — same idiom as useIsMobile.ts.
    if (typeof artQuery.addEventListener === 'function') {
      artQuery.addEventListener('change', onArtQueryChange);
    } else if (typeof artQuery.addListener === 'function') {
      artQuery.addListener(onArtQueryChange);
    }
    if (artRendered.value) startArtWork();
  });

  onBeforeUnmount(() => {
    clearAlertTimer();
    clearBootTimer();
    if (!artQuery) return;
    if (typeof artQuery.removeEventListener === 'function') {
      artQuery.removeEventListener('change', onArtQueryChange);
    } else if (typeof artQuery.removeListener === 'function') {
      artQuery.removeListener(onArtQueryChange);
    }
    artQuery = null;
  });

  const shipSrc = computed(() => {
    if (shipState.value === 'alert') return shipAlertArt;
    if (shipState.value === 'orbit') return shipOrbitArt;
    return shipSleepArt;
  });

  const shipAlt = computed(() => {
    if (shipState.value === 'alert') return "Edson's saucer waking up as the wallet connects";
    if (shipState.value === 'orbit') return "Edson's saucer orbiting a giant coin";
    return "Edson's saucer asleep beside a giant coin";
  });
```

⚠️ `armBootFallback` is referenced by `startArtWork` above its own declaration. `const` arrow functions are **not** hoisted — but `startArtWork` only *calls* it at mount, long after both bindings are initialised, so this is legal. If ESLint's `no-use-before-define` objects, move `armBootFallback` above `startArtWork`; do not convert either to a `function` declaration.

- [ ] **Step 5: Recompute the headline-measure table**

The comment above the `<h1>` (`HomeHero.vue:229–234`) is written against the old art width and #138's spec explicitly says to recompute it if the art is ever re-widened. This card re-widens it (163 px → 204 px at 640 px).

```html
        <!-- Press Start 2P advances a full em per character, so 14 characters need ~14x the font size.
             "FIRST WORD OUT" therefore needs ~266px at 19px, ~308px at 22px and ~364px at 26px —
             against a copy column of ~302px @360, ~384px @640, ~450px @768 and ~880px @1366.
             ⚠️ RECOMPUTED for the new art (Trello #145): the two-layer coin renders 204px wide at
             `sm` and 260px at `md`, against the mode sprite's 121px / 153px, so this card cost the
             copy column ~41px at 640px. 384px against a 266px headline still holds two lines with
             118px to spare — but that margin is now the tightest on the table. RECOMPUTE IT AGAIN if
             the art height moves off 138/176 or a `basis-[…]` comes back on either column. -->
```

- [ ] **Step 6: Replace the art column markup**

Replace the whole art block (`HomeHero.vue:328–344`, the comment and the `<div class="hidden shrink-0 …">`).

```html
    <!-- THE ART — unframed on purpose, sitting directly on App.vue's starfield; a border would turn the
         mascot into a product photo. TWO LAYERS on ONE shared 740x500 canvas: because the delivered
         files are pre-registered on that canvas, `inset-0` is the WHOLE of the ship's positioning —
         no offsets, no object-position, no scale.
         The COIN is the sizing layer and stays in FLOW: `h-[…] w-auto`, never a width cap, so the
         column's width stays a function of its height. 176px tall is a 260px canvas of which the coin
         itself is 59.46% = 155px — just over the 153px the sprite it replaces occupied, which is the
         floor AC13 sets. The 5px window [174,178] is arithmetic, not taste: below it the coin reads
         SMALLER than the placeholder, above it AC13's ceiling breaks. Do not nudge either number
         without re-measuring the band in BOTH states.
         The glow lives on the COIN (see the scoped style) — the ship carries no filter.
         Do NOT copy /how-to-play's `object-contain scale-[0.78] -translate-y-[4%]`: those numbers
         exist because GuideRail drops its sprite into a fixed 4:3, 172px-wide slot. No `loading="lazy"`
         either — this is above-the-fold art. -->
    <div class="relative hidden shrink-0 justify-center sm:flex">
      <!-- Decorative once the ship carries the description: two images with two descriptions of one
           picture is worse for a screen reader than one. -->
      <img
        :src="coinArt"
        alt=""
        aria-hidden="true"
        draggable="false"
        class="hero-art pixelated block h-[138px] w-auto md:h-[176px]"
      />
      <!-- ⚠️ THE `:key` IS LOAD-BEARING — see `alertRun` in the script. Keying on shipState alone
           would not remount for a SECOND alert, and ship-alert.gif would render its frozen last
           frame. It fails silently and looks like a broken wallet. -->
      <img
        :key="`${shipState}-${alertRun}`"
        :src="shipSrc"
        :alt="shipAlt"
        draggable="false"
        class="pixelated absolute inset-0 block h-full w-full"
      />
    </div>
```

- [ ] **Step 7: Update the scoped style's comment**

The values do **not** change — the coin renders 155 px wide at `md`, essentially the 153 px the radii were tuned for. Only the comment moves to the coin layer.

```css
<style scoped>
  /* THE COIN LAYER ONLY (Trello #145). Two FLAT drop-shadows — no `filter: blur`, no
     `backdrop-filter`, no `mix-blend-mode`, no animation — so html.light-graphics renders it
     unchanged. Putting it on the STATIC layer rather than the wrapper is deliberate: the browser
     computes the shadow once instead of re-deriving it on every GIF frame, and the halo's silhouette
     no longer changes shape as the ship travels across the canvas. The radii were tuned for a ~153px
     sprite and the coin lands at ~155px, so they carry over unchanged. */
  .hero-art {
    filter: drop-shadow(0 0 26px color-mix(in oklab, var(--color-cyan) 26%, transparent))
      drop-shadow(0 8px 16px color-mix(in oklab, black 55%, transparent));
  }
</style>
```

- [ ] **Step 8: Normalise line endings, then lint and build**

```bash
cd C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front
unix2dos src/components/home/HomeHero.vue
od -c src/components/home/HomeHero.vue | head -3     # expect \r \n pairs
yarn lint
yarn build
```

Expected: `yarn lint` clean (in particular **no** `no-unused-vars` on `modeSprite` — that import must be gone, and **no** `Insert ␍`), `yarn build` green.

- [ ] **Step 9: Commit**

```bash
cd C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front
git add src/components/home/HomeHero.vue
git commit -m "Render the home hero's two-layer art and its wallet state machine."
```

---

## Task 2: Delete the orphaned assets and repoint the stylesheet comment

**Files:**
- Delete: `src/assets/edson-ship.gif`, `src/assets/edson-ship.png`, `src/assets/coin.png`
- Modify: `src/assets/styles/arcade.css` (≈1106–1111, comment only)
- Test: none — verified by `git grep` and `yarn build` in Step 4.

**Interfaces:**
- Consumes: nothing from Task 1 at runtime. Ordered after it only because Task 1 removes the last textual mention of `edson-ship.*` (the header comment at `HomeHero.vue:29`), which is what makes the Step 3 grep return clean.
- Produces: nothing. No import anywhere in `src/` or `public/` refers to any of the three files — verified against the live tree.

- [ ] **Step 1: Delete the three files**

All three are verified unreferenced. `#138` kept the two `edson-ship.*` so a swap back stayed a two-line revert — a path this card closes. `src/assets/coin.png` (480×500, 4.6 KB) is a **different file** from the delivered `hero/coin.png` and has zero references anywhere.

```bash
cd C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front
git rm src/assets/edson-ship.gif src/assets/edson-ship.png src/assets/coin.png
```

Webpack already emits none of the three, so this is a **repo-size win only, not a bundle change** — do not claim a wire-size saving for it in the report.

- [ ] **Step 2: Rewrite the light-graphics comment in `arcade.css`**

Find the block above `html.light-graphics .character-img`. **The selector list is correct and must not change** — and do **not** add a `html.light-graphics .hero-art` rule; the entire point is that this class stays *excluded* from the `filter: none` kill.

```css
/* Per-element drop shadows on the lobby/picker art (rest + hover).
   NOT the starting page's hero art: `.hero-art` is now the STATIC COIN layer of the two-layer hero
   composite (Trello #145, was the borrowed mode sprite under #138). Its glow is two flat
   drop-shadows with no blurred backdrop and no animation, and because it sits on the static layer
   the browser computes it ONCE rather than re-deriving it on every frame of the ship GIF above it —
   which is a stronger case on these devices than the one it replaced, not a weaker one. It stays.
   (This rule has now outlived three names for that art: `.hero-mascot`, `.hero-ship`, and the mode
   sprite. Repoint it here; do not add a rule for a class no component renders.) */
html.light-graphics .character-img,
html.light-graphics .pixelated-card:hover .character-img {
  filter: none;
}
```

- [ ] **Step 3: Verify nothing references the deleted files**

```bash
cd C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front
git grep -n "edson-ship\|assets/coin"
```

Expected: **no output at all**. Any hit means Task 1 Step 3's comment rewrite missed a mention, or a real import survived.

- [ ] **Step 4: Normalise line endings, then lint and build**

```bash
cd C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front
unix2dos src/assets/styles/arcade.css
yarn lint
yarn build
```

Expected: both clean. A build failure here means one of the three files was imported after all — restore it and re-check the grep.

- [ ] **Step 5: Commit**

```bash
cd C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front
git add -A src/assets
git commit -m "Delete the orphaned hero assets and repoint the light-graphics note."
```

---

## Task 3: Measured browser QA

**Files:** none modified. This task produces evidence, and sends any failure back to Task 1.

**Interfaces:**
- Consumes: the built app from Tasks 1–2.
- Produces: measured numbers for AC12/AC13/AC14, and pass/fail on the state machine's eight behavioural ACs.

⚠️ **Two ports are in play.** Other worktrees frequently hold 8081. Pick a **verified-free** port and confirm the served bundle is *this* worktree's (grep the served JS for `hero/coin` or `shipState`) before trusting anything you see.

- [ ] **Step 1: Serve this worktree**

```bash
cd C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front
cp ../../../monkify-front/.env.local .    # gitignored; without it the app is "game unavailable"
cp ../../../monkify-front/yarn.lock .     # if absent from the worktree
yarn install --frozen-lockfile
yarn serve --port <verified-free-port>
```

- [ ] **Step 2: Cold desktop state (AC1, AC16, AC17)**

At ≥ 1024 px with **no** wallet and `walletName` cleared from Local Storage. Expect: coin visible, ship asleep and looping. In Elements — the coin `<img>` carries `.hero-art` and both drop-shadows; the ship `<img>` has **no** `filter`; exactly **one** GIF is in the DOM.

- [ ] **Step 3: Sizing, measured in BOTH states (AC12, AC13)**

⚠️ **AC12 is no longer true by construction** — at 176 px the art, not the copy, drives the *connected* band. It must be measured, and the connected state is now the worst case.

Emulate **1366×768** and run in the console, cold and then connected:

```js
const band = document.querySelector('main section');
const coin = document.querySelector('.hero-art');
console.log({
  band: band.getBoundingClientRect().height,          // must be <= 265
  artHeight: coin.getBoundingClientRect().height,     // must be within [140, 178]
  coinWidth: coin.getBoundingClientRect().width * 0.5946, // must be >= 153
});
```

Expected: band ≈ 250 px cold and ≈ 248 px connected (both ≤ 265), `artHeight` 176, `coinWidth` ≈ 155.

- [ ] **Step 4: Headline measure at 640 px (AC14)**

Emulate exactly **640 px** wide, cold. `FIRST WORD OUT` / `TAKES THE POT.` must still occupy **exactly two** lines. Three lines means the widened art took too much of the copy column — reduce the `sm` height, not the `md` one.

- [ ] **Step 5: The orbit loop and the empty frame (AC9, AC10)**

Watch one full 1500 ms orbit cycle. The ship passes **over** the coin; the coin stays visible through the ship's transparent pixels; at the 700 ms frame the **coin alone** shows — no broken-image icon, no white box, no layout shift. ⚠️ Frame 7's emptiness was inferred from its dirty rect, not a pixel decode — **this one must be seen, not assumed.**

- [ ] **Step 6: Light graphics (AC15)**

At a width **in 640–1023 px** — not a forced class on a desktop; this is the range QA skips. The ship must still animate in every state, and computed styles must show no `backdrop-filter` / `blur()` / `mix-blend-mode` / infinite animation in the band.

- [ ] **Step 7: Phone (AC11)**

390×844: **no art at all**, headline line breaks unchanged from today. Confirm in the Network panel that none of the four hero assets was fetched, and that no timer was allocated.

- [ ] **Step 8: The wallet states (AC2–AC8, AC18) — needs a real wallet**

These are the owner's to drive in Step 9 hand-off if no wallet is available to the implementer. In order:
1. **Deliberate connect (AC2, AC18):** with Network open, click Connect. The alert plays in full and hands off to orbit at 1200 ms with **no** blank frame. Confirm alert/orbit were fetched **before** the click (the preload), not at the swap.
2. **⚠️ Second connect (AC5) — do not skip.** Disconnect, connect again. The alert must play **again, in full**. A frozen frame means the `:key` is not bumping.
3. **Auto-reconnect (AC3):** with `walletName` persisted, hard-reload. The hero must open **in orbit** — no sleep frame, no alert. Use the devtools screenshot filmstrip if the eye is not sure. *(This is the amended behaviour — the spec's own snippet would fail it.)*
4. **Interrupts (AC4, AC6, AC7, AC8):** disconnect while orbiting → immediate cut to sleep. Connect then disconnect **within 1.2 s** → sleep, never orbit. Connect then navigate to `/game` within 1.2 s → **no console error**. Toggle ~10× rapidly → the final state matches the wallet.

- [ ] **Step 9: Guide untouched (AC20)**

`/how-to-play` → **PICK A MODE** still renders `game-modes.gif`, unchanged.

---

## Task 4: Final gates

**Files:** none modified.

- [ ] **Step 1: Confirm the backend is untouched (AC21)**

```bash
git -C C:/Users/kurok/Desktop/monkify/monkify-backend status --short --branch
git -C C:/Users/kurok/Desktop/monkify/monkify-backend diff main --stat
```

Expected: on `main`, clean, empty diff. `monkify-backend` gets **no branch and no worktree** for this card.

- [ ] **Step 2: Re-run both gates on the final tree (AC21)**

```bash
cd C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front
yarn lint && yarn build
```

- [ ] **Step 3: Push the branch**

```bash
git -C C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-front push origin feature/home-hero-sprite
git -C C:/Users/kurok/Desktop/monkify/.worktrees/home-hero-sprite/monkify-meta push origin feature/home-hero-sprite
```

⚠️ **Only after the owner has tested and approved** (`monkify-implement-feature` Step 9). Never force-push.

---

## Self-Review

**Spec coverage.** §4 assets — already committed, Task 1 Step 2 imports all four. §7.1/§7.2/§7.3 mechanism — Task 1 Step 4, with the §7.3.1 gap amended per the owner. §7.3.5 the 640 px gate and §7.3.6 preload — Task 1 Step 4 (`artRendered`, `startArtWork`). §7.4 sizing — Task 1 Steps 5–6 (`h-[138px] md:h-[176px]`), measured in Task 3 Steps 3–4. §7.5 markup — Task 1 Step 6. §7.6.1 the dead import — Task 1 Step 2. §7.6.2 three deletions — Task 2 Step 1, grep-verified Step 3. §7.6.3 `arcade.css` — Task 2 Step 2. §7.6.4 the header comment — Task 1 Step 3. §7.7 light-graphics range — Task 3 Step 6. §10.2 rows 10–12 (`HomePage.vue`, `wallet.ts`, `walletDeepLink.ts` expected unchanged) — confirmed already, no task needed. §10.3 KB rows 3–6 are ship-time, correctly out of this plan. §11 gates — Task 1 Step 8, Task 4 Step 2. All 21 ACs map to a step; AC19 → Task 2 Steps 1+3, AC21 → Task 4 Steps 1–2.

**Placeholder scan.** No TBDs. Every code step carries the literal code; every verification step names the command and its expected output.

**Type consistency.** `ShipState` is declared once and used by `shipState` only. Asset bindings are `coinArt` / `shipSleepArt` / `shipAlertArt` / `shipOrbitArt` throughout Steps 2, 4 and 6 (suffixed `…Art` so `shipAlertArt` never reads as the `alert` *state*). `clearAlertTimer` / `clearBootTimer` / `armBootFallback` / `startArtWork` / `onArtQueryChange` each appear with one spelling. `artQuery` is the `MediaQueryList` and `ART_QUERY` the string — deliberately distinct.

**One residual risk, recorded:** `startArtWork` references `armBootFallback` before its `const` declaration. Legal at call time (mount), but if the repo's ESLint config enables `no-use-before-define` for variables, Step 8's lint will catch it — the fix is a move, not a redesign, and Step 4 says so.
