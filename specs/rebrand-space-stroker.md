# ChimpOut → "Space Stroker" — Frontend Rebrand — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Change app name to "Space Stroker"](https://trello.com/c/qbcZgHuX) (2026-07-20).
> Repos affected: `monkify-front`, `monkify-lp`, and this doc in `monkify-meta`.
> **Backend (`monkify-backend`) is intentionally NOT touched** — see §6.
>
> **Purpose:** Rename the product's **consumer-facing brand** from **ChimpOut** to **Space Stroker**
> everywhere a player or an integrator meets the frontend (the game SPA `monkify-front` and the
> landing page `monkify-lp`), and do a full editorial pass on user-facing text while in there. The
> monkey/space theme (Edson the space chimp, all art, the story, the "monkey" taglines) is retained
> unchanged — only the brand proper noun changes. The backend, DB, API host and infra keep the name
> `ChimpOut` for now; a separate follow-up card ([#75](https://trello.com/c/VgNMpTzu)) tracks renaming
> those later.

---

## 1. Summary

A **clean-break** rename: every occurrence of the brand token `ChimpOut` (in any casing — `ChimpOut`,
`chimpout`, `CHIMPOUT`, the split-`<span>` wordmark) and the two stray `Monkify`/`$MONKIFY` leftovers
becomes **Space Stroker** / `space-stroker` / `$SPACE STROKER` as appropriate to context. No aliases,
no redirects, no dual-emit — safe because **nothing is live** (see §3, decision 2).

The brand appears in six distinct categories, each with a different target form:

| # | Category | Old form | New form |
| --- | --- | --- | --- |
| 1 | **Display copy / wordmark** | `CHIMP`·`OUT` (split colour), `CHIMPOUT`, `ChimpOut` | `SPACE`·`STROKER` (split colour), `Space Stroker` |
| 2 | **Page metadata** (`<title>`, meta description) | `ChimpOut …` | `Space Stroker …` |
| 3 | **Route prefix** | `/chimpout/*` | `/space-stroker/*` |
| 4 | **postMessage integration contract** | `source: 'chimpout'` / `'chimpout-wallet'` | `source: 'space-stroker'` / `'space-stroker-wallet'` |
| 5 | **Client storage keys** | `chimpout.*` | `space-stroker.*` |
| 6 | **Code identifiers / CSS / package** | `.chimpout-toast*`, `EMBED_SOURCE`, `package.json name` | `.space-stroker-toast*`, same consts (value only), `space-stroker` |

**Two mechanical traps** a blind find/replace misses (call-outs repeated in §7):

- `monkify-front/src/components/GameHeader.vue` renders the wordmark **split across two `<span>`
  nodes** (`CHIMP` + `<span>OUT</span>`), so a `ChimpOut` text search never finds it.
- `monkify-front/public/verify.html` uses the **all-caps solid** form `CHIMPOUT` in the `<h1>`.

**Deliberately NOT renamed** (kept accurate on purpose): the `// Mirrors backend ChimpOut.*` code
comments that name real backend C# namespaces (`monkify-front/src/types/index.ts` ×4,
`src/utils/provablyFair.ts` ×1, `public/verify.html:16`) — the backend keeps the name `ChimpOut`, so
these comments stay correct. Renaming them would make them *wrong*.

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there):

- **Worktree-per-repo / branch-per-repo:** `feature/rebrand-space-stroker` in each affected repo, work
  done in `.worktrees/rebrand-space-stroker/<repo>`; hubs stay on `main` (`monkify-lp` on `master`).
- **No backend tests to change:** this card touches **no** backend code, so `ChimpOut.Tests` is not
  run and no unit test changes. **`monkify-front` has no unit-test harness** — verification is
  `yarn lint` + `yarn build` (front) and `npm run build` (LP), plus browser QA (see §11, §13).
- **Level = Minor change** (rebrand/refactor, no game-flow change) ⇒ **no backend integration test**
  and **no new `features/*.md`** doc (per `constraints.md` → "Integration tests are mandatory for
  every new feature", which explicitly exempts minor changes / refactors). This matches how the
  earlier `Monkify → ChimpOut` rebrand (`specs/rebrand-chimpout.md`) was classified.
- **Secrets/config:** any `.env*` touched keeps **mock/placeholder** values only.
- **EF / migrations / DB:** N/A — no backend, no schema change.
- **Frontend env vars use `VUE_APP_*`** (Vue CLI), not `VITE_*`.

---

## 3. Resolved product decisions

| # | Decision |
| --- | --- |
| 1 | **New brand string is `Space Stroker`** (two words, confirmed exact). Slug/machine form is `space-stroker` (kebab-case); token-symbol form (anti-scam copy) is `$SPACE STROKER`. |
| 2 | **Clean break — rename everything, no aliases/redirects.** Justified because nothing is live: LP `PLAY_URL` is a `PLACEHOLDER` host, the production domain is undecided, no casino has integrated against the embed contract, and no player has a persisted provably-fair seed (real `terraform apply` is still a pending ops step). If any of those turns out false before merge, the route / `EMBED_SOURCE` / `clientSeed` renames must be revisited with redirects + a key-migration shim. |
| 3 | **Scope includes the three normally-frozen contracts:** the route prefix (`/chimpout/*` → `/space-stroker/*`), the postMessage `source` values, and the client-storage key prefix. |
| 4 | **Wordmark = two words, split colour:** `SPACE` in the default ink + `STROKER` in primary orange, mirroring the old `CHIMP·`**`OUT`** treatment. |
| 5 | **Mascot & lore stay 100% as-is:** Edson the space chimp, all art/GIFs, the 6-slide `EDSON.LOG` story, the `BET ON THE MONKEY` footer tagline, `Gamble like a monkey`, and the `infinite monkey` lines are **not** touched. No new assets. The word "chimp" survives **only** where it means the character (e.g. "the space chimp"), never as the brand. |
| 6 | **Full editorial pass on user-facing text** in both apps (text only — no layout/component change): fix rename fallout, typos, tone drift, and copy that contradicts how the product actually works, plus repo docs. |
| 7 | **Backend/infra drift is intentional and temporary** (first step). The `// Mirrors backend ChimpOut.*` comments stay accurate (not renamed). Follow-up card [#75](https://trello.com/c/VgNMpTzu) tracks the backend+infra rename. |
| 8 | **`TODO(ship)` placeholders are NOT filled** — real app URL/domain and social handles stay flagged placeholders; only the path segment in `PLAY_URL` becomes `/space-stroker/game` so LP↔app paths match. |

---

## 4. Data model

**N/A — frontend-only.** No entity, enum, EF mapping, or migration change. No DB touch.

---

## 5. Configuration

| Item | File | Change |
| --- | --- | --- |
| Front package name | `monkify-front/package.json` | `"name": "ChimpOut"` → `"name": "space-stroker"` (repo stays `"private": true`). |
| LP package name | `monkify-lp/package.json` + `package-lock.json` | `"monkify-lp"` → `"space-stroker-lp"` (update both lock entries; keep versions/deps untouched). |
| LP play URL | `monkify-lp/src/pages/index.astro:82`, `src/components/Header.vue:7` | `PLAY_URL` path `…/chimpout/game` → `…/space-stroker/game`; the `PLACEHOLDER-app-url` host and the `// TODO(ship)` flag **stay** (only reword "real ChimpOut app URL" → "real Space Stroker app URL"). |
| Front env prose | `monkify-front/.env.example` | Brand prose `ChimpOut frontend build config` → `Space Stroker …`; `/chimpout/game` mention → `/space-stroker/game`; the `api.chimpout.com` production hint **stays** (backend host, unchanged); fix the `monkify-meta/deploy` leftover wording only if it names the brand (repo folder name itself is tooling → leave). Values remain mock/placeholder. |

No `GeneralSettings`, no seed data, no new env var.

---

## 6. Backend behaviour

**None. `monkify-backend` is not modified by this card.** The API host, admin portal, SignalR hub
names, C# namespaces (`ChimpOut.*`), DB, `chimpout.com` / `api.chimpout.com`, and the Terraform/GCP
infra all keep the name ChimpOut. The frontend continues to call the same endpoints, send the same
`X-Launch-Token` / `X-Admin-Token` headers, and read the same DTO shapes — none of those carry the
brand token, so nothing on the wire changes. (Backend rename is follow-up card #75.)

---

## 7. Frontend behaviour

### 7.1 `monkify-front` (the game SPA)

**Routes — `src/routes.ts`.** Replace every `/chimpout/` path segment with `/space-stroker/`:
`/chimpout/game`, `/chimpout/game/lobby`, `/chimpout/game/play`, `/chimpout/admin`,
`/chimpout/wallet-bridge`, and the `/` → `/chimpout/game` redirect. `/error` is unbranded — leave.
Update the route-path mentions in the file's own comments too. **Also** the one **runtime-built** path
outside the router: `src/hooks/useEmbeddedWallet.ts:28` — `` `${window.location.origin}/chimpout/wallet-bridge` ``
→ `/space-stroker/wallet-bridge` (must stay in sync with the route).

**Integration identifiers (the embed contract).**
- `src/utils/embed.ts:1` — `EMBED_SOURCE = 'chimpout'` → `'space-stroker'` (the const **name** stays;
  only the string value changes). This is the value every embedding casino filters on
  (`msg.source === 'space-stroker'`).
- `src/utils/walletBridge.ts:4` — `WALLET_BRIDGE_SOURCE = 'chimpout-wallet'` → `'space-stroker-wallet'`.
- `src/hooks/useEmbeddedWallet.ts:31` — the popup **window name** `window.open(url, 'chimpout-wallet', …)`
  → `'space-stroker-wallet'`.

**Client storage keys** (clean rename — no migration shim, nothing persisted in production):
- `src/hooks/useClientSeed.ts:10-11` — `chimpout.clientSeed`, `chimpout.clientSeed.nonce`
- `src/utils/launchToken.ts:8,11` — `chimpout.launchToken`, `chimpout.autoLaunch`
- `src/utils/adminToken.ts:8` — `chimpout.adminToken`
- `src/components/GameHeader.vue:44` — `chimpout.audio`
→ all `chimpout.*` → `space-stroker.*`.

**User-visible copy / metadata.**
- `src/components/GameHeader.vue:100` — the **split-`<span>` wordmark** `CHIMP<span…>OUT</span>` →
  `SPACE<span…>STROKER</span>` (⚠️ trap #1 — not text-searchable as `ChimpOut`).
- `public/index.html:8` — `<title>ChimpOut</title>` → `<title>Space Stroker</title>`.
- `public/verify.html` — `:6` `<title>`, `:10`/`:11` prose ("reproduces a ChimpOut round…", "with NO
  call back to ChimpOut"), `:87` `<h1>&gt; CHIMPOUT PROVABLY-FAIR VERIFIER (OFFLINE)</h1>` (⚠️ trap
  #2 — all-caps solid). **Leave `:16`** (`ChimpOut.Domain.Games.Services.ProvablyFair` — backend
  namespace).
- `public/embed-example.html` — `:6` `<title>`, `:154` `<h1>`, `:190` iframe `title="ChimpOut game"`,
  the demo-copy URLs `:174`/`:180`, the built URLs `:215`/`:217` (`/chimpout/game…` → `/space-stroker/game…`),
  and the `source` guard `:258` (`data.source !== 'chimpout'` → `'space-stroker'`). This is a dev/demo
  harness but it is the reference an integrator copies, so it must match the new contract.
- `src/pages/WalletBridgePage.vue:122` — "Open this window from the **ChimpOut** game to place a bet."
  → "…from the **Space Stroker** game…".

**CSS / build.**
- `src/main.css` — the 10 `.chimpout-toast*` selectors → `.space-stroker-toast*` (keep the
  Toastify/v3-toastify suffixes intact).
- `src/main.ts:48-49` — `toastClassName: 'chimpout-toast'`, `bodyClassName: 'chimpout-toast__body'`
  → `space-stroker-toast` / `space-stroker-toast__body` (must match the renamed CSS).
- `package.json:2` — see §5.

**`verify.html` ↔ `provablyFair.ts` parity.** These two intentionally duplicate the keystream
algorithm byte-for-byte (they say "KEEP IN SYNC"). This rename touches **only brand strings** in
`verify.html` and **nothing** in `provablyFair.ts` (its single brand token is the backend-namespace
comment, which we leave). So parity is preserved by construction — do not alter the algorithm.

**Docs / comments (editorial).**
- `EMBEDDING.md` (16 occurrences) — the operator-facing contract doc: `# Embedding ChimpOut…`, the
  `id="chimpout"` / `title="ChimpOut"` iframe attrs, every `source: 'chimpout'` / `msg.source !== 'chimpout'`,
  and the `/chimpout/…` URLs → all Space Stroker / `space-stroker`.
- `README.md:1`, `src/assets/styles/README.md:1`, `src/assets/styles/colors.css:1` — brand in the
  heading/comment → Space Stroker.
- `.env.example` — see §5.
- `CLAUDE.md` — the `# monkify-app (Front)` heading Monkify leftover **and** stale content (it still
  describes `SessionSelectPage`/`useSession`/`SessionStatus`, all renamed to `Game*`) → refresh to
  current names + brand. (Editorial; not shipped to users.)
- Comment-only route mentions in `src/constants/featureFlags.ts`, `src/hooks/useAdmin.ts`,
  `src/hooks/useLaunch.ts`, `src/components/header/DevAdminButton.vue`,
  `src/pages/DevLandingPage.vue` — update `/chimpout/*` → `/space-stroker/*` for accuracy.
- `src/components/header/PlayerCustomizationModal.vue:186` — comment "never sent to ChimpOut" →
  "never sent to Space Stroker" (it's a comment, not visible copy, but it's brand prose).

**Deliberately left intact:** `src/types/index.ts` (all 4 `// Mirrors backend ChimpOut.*` comments)
and `src/utils/provablyFair.ts:5` (same). These name real backend namespaces that keep the ChimpOut
name.

### 7.2 `monkify-lp` (the landing page)

- **Wordmark (split colour):** `src/pages/index.astro:151`, `src/components/Header.vue:83`,
  `src/components/Footer.vue:11` — `CHIMP<span…>OUT</span>` → `SPACE<span…>STROKER</span>`.
- **Footer copyright:** `src/components/Footer.vue:18` — `© 2026 CHIMPOUT` → `© 2026 SPACE STROKER`.
- **Page metadata:** `src/layouts/Layout.astro:7` title `"CHIMPOUT — Bet on the space chimp"` →
  `"Space Stroker — Bet on the space chimp"` (keep the "space chimp" lore). `:9` meta description keeps
  its "Edson the space chimp" lore unchanged (no brand token there).
- **Hero copy / alt:** `src/pages/index.astro:197` `alt="The ChimpOut arcade coin"` → `"The Space
  Stroker arcade coin"`.
- **Token / anti-scam section:** `src/pages/index.astro:337` "…ChimpOut runs on real USDC…" →
  "…Space Stroker runs on real USDC…"; `:352` `"$CHIMPOUT"` → `"$SPACE STROKER"` (the anti-scam
  warning about a fake token).
- **Roadmap:** `src/components/SpaceRoadmap.vue:44` `'ChimpOut is live — real USDC bets on Solana'`
  and `:95` `'ChimpOut in your pocket'` → Space Stroker.
- **Animated typewriter word:** `src/components/HowItWorksDisplay.vue:13` `const WORD = "CHIMPOUT"` →
  **`"SPACE STROKER"`** (with the space — the CRT types char-by-char and renders the space fine; if
  the fixed housing clips it in QA, fall back to `"SPACESTROKER"`). Update the describing comment `:11`.
- **Design-system header comment:** `src/styles/global.css:4` `CHIMPOUT — Arcade design system` →
  `Space Stroker — …`.
- **`PLAY_URL`:** see §5 (path only; placeholder host + `TODO(ship)` stay).
- **Package name:** see §5.
- **Docs (editorial):** `PRODUCT.md` — **fix the `$MONKIFY` leftovers** (`:14` "play or buy $MONKIFY",
  `:23` "Buy $MONKIFY") which contradict the live token-free / anti-scam copy → reword to the
  token-free reality with the new name; `:18` "Monkify" → Space Stroker; keep the "Edson, the space
  chimp" lore. `CLAUDE.md` (LP) and `README.md` (Astro starter boilerplate) — refresh brand/prose.
- **Left intact:** cross-repo code comments that name the **repo folder** `monkify-front` (e.g.
  `index.astro:18`, `HowItWorksDisplay.vue:10`, `HowItWorksLever.vue:16`) — repo/tooling names are out
  of scope, not brand.

### 7.3 Behaviour unchanged

No game flow, no network call, no component structure, no styling values change. The rename is
string-level. Flows (bet → run → reward, embed lifecycle, wallet bridge) are identical to today; no
sequence diagram needed.

---

## 8. Flow diagrams

N/A — no flow changes (see §7.3).

---

## 9. Out of scope

- Any **backend / DB / infra / C# namespace / domain** rename → follow-up card
  [#75](https://trello.com/c/VgNMpTzu).
- **Filling the `TODO(ship)` placeholders** — real production app URL/domain and real social handles
  (Twitter/Telegram/Discord). They stay flagged placeholders; only the `PLAY_URL` **path** becomes
  `/space-stroker/game`.
- **New mascot / art / GIFs / story rewrite** — the monkey/space theme is retained unchanged; no new
  assets.
- **Renaming repo folders / tooling** (`monkify-front`, `monkify-lp`, `monkify-*` skills, the MonkAIfy
  board) and the `// Mirrors backend ChimpOut.*` namespace comments.
- No `features/*.md` doc and no backend integration/unit test (Minor change, frontend-only).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None** — no backend files change.

### 10.2 Frontend — `monkify-front`

| # | File | Change |
| --- | --- | --- |
| 1 | `src/routes.ts` | All `/chimpout/*` route paths + the `/` redirect + comment mentions → `/space-stroker/*`. |
| 2 | `src/hooks/useEmbeddedWallet.ts` | Runtime-built `…/chimpout/wallet-bridge` URL (`:28`) + popup window name `'chimpout-wallet'` (`:31`) → space-stroker. |
| 3 | `src/utils/embed.ts` | `EMBED_SOURCE` value `'chimpout'` → `'space-stroker'`. |
| 4 | `src/utils/walletBridge.ts` | `WALLET_BRIDGE_SOURCE` value → `'space-stroker-wallet'`. |
| 5 | `src/hooks/useClientSeed.ts` | Storage keys `chimpout.clientSeed`, `.nonce` → `space-stroker.*`. |
| 6 | `src/utils/launchToken.ts` | Keys `chimpout.launchToken`, `chimpout.autoLaunch` → `space-stroker.*`. |
| 7 | `src/utils/adminToken.ts` | Key `chimpout.adminToken` → `space-stroker.adminToken`. |
| 8 | `src/components/GameHeader.vue` | **Split-`<span>` wordmark** (`:100`, trap #1) + `chimpout.audio` key (`:44`). |
| 9 | `src/main.css` | 10 `.chimpout-toast*` selectors → `.space-stroker-toast*`. |
| 10 | `src/main.ts` | `toastClassName`/`bodyClassName` → `space-stroker-toast*`. |
| 11 | `src/pages/WalletBridgePage.vue` | Visible copy `:122` (+ comment `:3` route) → Space Stroker / `/space-stroker/*`. |
| 12 | `public/index.html` | `<title>` → `Space Stroker`. |
| 13 | `public/verify.html` | `:6` title, `:10`/`:11` prose, `:87` all-caps `<h1>` (trap #2) → Space Stroker. **Leave `:16`** (backend namespace). |
| 14 | `public/embed-example.html` | Title/h1/iframe-title, demo + built `/chimpout/game…` URLs, `source` guard `:258` → space-stroker. |
| 15 | `EMBEDDING.md` | 16 brand occurrences (headings, `id`/`title`, `source` filters, URLs) → Space Stroker / `space-stroker`. |
| 16 | `package.json` | `"name"` → `"space-stroker"`. |
| 17 | `.env.example` | Brand prose + `/chimpout/game` mention → space-stroker; keep `api.chimpout.com` host hint + mock values. |
| 18 | `README.md`, `src/assets/styles/README.md`, `src/assets/styles/colors.css` | Brand in headings/comment → Space Stroker. |
| 19 | `CLAUDE.md` | Monkify leftover heading + stale `Session*` content → refresh to `Game*` + Space Stroker. |
| 20 | `src/constants/featureFlags.ts`, `src/hooks/useAdmin.ts`, `src/hooks/useLaunch.ts`, `src/components/header/DevAdminButton.vue`, `src/pages/DevLandingPage.vue` | Comment route mentions `/chimpout/*` → `/space-stroker/*`. |
| 21 | `src/components/header/PlayerCustomizationModal.vue` | Comment "never sent to ChimpOut" (`:186`) → Space Stroker. |
| — | `src/types/index.ts`, `src/utils/provablyFair.ts` | **DO NOT EDIT** — their only brand tokens are the `// Mirrors backend ChimpOut.*` namespace comments (kept accurate). |

### 10.3 Frontend — `monkify-lp`

| # | File | Change |
| --- | --- | --- |
| 1 | `src/pages/index.astro` | Wordmark `:151`, coin alt `:197`, token copy `:337`, anti-scam `$CHIMPOUT` `:352`, `PLAY_URL` path `:82` + `TODO(ship)` wording `:81`. |
| 2 | `src/components/Header.vue` | Wordmark `:83`, `PLAY_URL` path `:7` + `TODO(ship)` wording `:6`. |
| 3 | `src/components/Footer.vue` | Wordmark `:11`, copyright `:18`. |
| 4 | `src/layouts/Layout.astro` | `<title>` `:7` (keep "space chimp" lore). |
| 5 | `src/components/SpaceRoadmap.vue` | Roadmap lines `:44`, `:95`. |
| 6 | `src/components/HowItWorksDisplay.vue` | `WORD` const `:13` → `"SPACE STROKER"` + comment `:11`. |
| 7 | `src/styles/global.css` | Design-system header comment `:4`. |
| 8 | `package.json` + `package-lock.json` | `"monkify-lp"` → `"space-stroker-lp"`. |
| 9 | `PRODUCT.md` | Fix `$MONKIFY` leftovers (`:14`, `:23`) + `Monkify` (`:18`) → token-free reality + Space Stroker. |
| 10 | `CLAUDE.md` (LP), `README.md` | Brand/prose refresh (editorial). |

### 10.4 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `specs/rebrand-space-stroker.md` | This spec (new). |
| 2 | `INDEX.md` | Add spec row (status Ready) + update "Feature currently in flight". |

---

## 11. Unit tests

**None to change.** No backend code is touched (`ChimpOut.Tests` untouched, not run). `monkify-front`
has **no unit-test harness**, and `monkify-lp` has none either. Verification is build/lint + browser
QA:

| Check | Command | Expected |
| --- | --- | --- |
| Front lint | `yarn lint` (in front worktree) | clean |
| Front build | `yarn build` | succeeds; bundle serves under `/space-stroker/*` |
| LP build | `npm run build` (in LP worktree) | succeeds |
| Residue sweep | case-insensitive `chimp\W?out` across both `src/` + `public/` | matches **only** the intentionally-kept `// Mirrors backend ChimpOut.*` namespace comments (front `types/index.ts`, `provablyFair.ts`, `verify.html:16`) |

---

## 12. Acceptance criteria

Mapped 1:1 from the Trello **Acceptance Criteria** checklist:

1. Given the game SPA is loaded, when the header, browser-tab title, wallet-bridge page and offline
   verifier (`verify.html`) render, then each shows "Space Stroker" (wordmark: `SPACE` default +
   `STROKER` primary orange) and no `ChimpOut`/`CHIMPOUT` brand token is visible anywhere.
2. Given a fresh browser, when the player navigates the app, then every route is served under
   `/space-stroker/*` (game, game/lobby, game/play, admin, wallet-bridge), the `/` redirect lands on
   `/space-stroker/game`, and no `/chimpout/*` path resolves (incl. the runtime-built wallet-bridge
   URL).
3. Given the app runs embedded, when it posts lifecycle messages to the parent, then every message
   carries `source: 'space-stroker'` (and the wallet bridge uses `'space-stroker-wallet'`), and
   `EMBEDDING.md` + `embed-example.html` instruct integrators to filter on `'space-stroker'` — no
   `'chimpout'` source value remains.
4. Given the app reads/writes client state, then all storage keys use the `space-stroker.*` prefix
   (clientSeed, clientSeed.nonce, audio, launchToken, autoLaunch, adminToken) and no `chimpout.*` key
   is read or written.
5. Given the LP is loaded, when the hero wordmark, header, footer, `<title>`, meta description,
   roadmap, token/anti-scam section and the animated typewriter word render, then all read "Space
   Stroker", the anti-scam line warns about a fake `$SPACE STROKER` (not `$CHIMPOUT`), and `PLAY_URL`
   points at `…/space-stroker/game` (host still a flagged `TODO(ship)` placeholder).
6. Given the monkey/space theme is retained, when the apps render, then Edson, all art/GIFs, the
   `EDSON.LOG` story, `BET ON THE MONKEY`, `Gamble like a monkey` and the `infinite monkey` lines are
   unchanged, and no new image/GIF asset was added.
7. Given the editorial pass, when repo docs/copy are reviewed, then contradictions are fixed — LP
   `PRODUCT.md` no longer says "buy $MONKIFY", front `CLAUDE.md` no longer references the old
   `Session*` names, and `.env.example` prose is corrected — while the `// Mirrors backend ChimpOut.*`
   comments are deliberately left intact.
8. Given the change is complete, when the frontends are built, then `yarn lint` + `yarn build`
   (front) and `npm run build` (LP) all pass, `package.json` name is `space-stroker` /
   `space-stroker-lp`, the LP's beast-mode work is preserved, and a case-insensitive `chimp\W?out`
   sweep returns only the intentionally-kept backend-namespace comments.

---

## 13. Manual QA

1. `cd .worktrees/rebrand-space-stroker/monkify-front && yarn install && yarn serve` → open
   `http://localhost:8080` — redirects to `/space-stroker/game`; header wordmark reads **SPACE STROKER**
   (STROKER orange); tab title "Space Stroker".
2. Open `public/verify.html` directly — `<h1>` reads `> SPACE STROKER PROVABLY-FAIR VERIFIER (OFFLINE)`.
3. Open `public/embed-example.html` — the iframe `src` uses `/space-stroker/game`; the message filter
   checks `data.source === 'space-stroker'`.
4. DevTools → Application → Local/Session Storage: keys are `space-stroker.*`.
5. `cd .worktrees/rebrand-space-stroker/monkify-lp && npm install && npm run dev` →
   `http://localhost:4321` — hero/header/footer wordmark SPACE STROKER; token section anti-scam line
   says `$SPACE STROKER`; the how-it-works CRT types **SPACE STROKER**; beast-mode card still hover-animates.
6. Residue sweep: `grep -rin "chimp" src/ public/` in each worktree → only the kept namespace comments.

---

## 14. Trello / pipeline

- **Source card:** [Change app name to "Space Stroker"](https://trello.com/c/qbcZgHuX) (#74) — moved
  to **Specs created**; **Implementation** section on the card lists worktree + branch + spec.
- **Follow-up:** [#75](https://trello.com/c/VgNMpTzu) — backend + infra rename (separate, un-refined).
- **Next:** `/monkify-implement-feature` on this card to build it; then `/monkify-ship-feature` to
  merge each `feature/rebrand-space-stroker` branch to its default branch (front/meta → `main`, LP →
  `master`) and sync the KB.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-20 | Initial spec from refined card #74 (frontend-only ChimpOut → Space Stroker rebrand). |
