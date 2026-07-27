# Feature — Analytics (GA4 on the landing page and the app)

> Status: **shipped.** Authoritative spec: `../specs/analytics.md`
> (merged via `feature/analytics` on 2026-07-27).
> **Frontend-only:** `monkify-front` + `monkify-lp`. `monkify-backend` is not involved — no endpoint,
> entity, migration or config. Trello #101 ([Hbfp779A](https://trello.com/c/Hbfp779A)).

## What it does

Both public properties — the landing page (`www.spacestroker.com`) and the game app
(`app.spacestroker.com`) — report to **one GA4 property** so we can answer how many people arrive,
which campaign or site sent them, and whether they got as far as connecting a wallet and placing a
bet. Because the two hosts share the registrable domain `spacestroker.com`, GA4 keeps a **single
session across the LP→app hop**, so a bet is attributed to the campaign that brought the visitor
rather than to "referral / www.spacestroker.com". Measurement is consent-gated, carries no money and
no player identity, and is switched off entirely wherever the data would be noise.

## Core rules

**Enablement**
- The GA4 tag loads only when a measurement id was baked in at **build** time:
  `VUE_APP_GA_MEASUREMENT_ID` (app, Vue CLI) and `PUBLIC_GA_MEASUREMENT_ID` (LP, Astro/Vite). Both
  carry the **same** id — one property, one data stream.
- An **empty or unset id means analytics does not exist**: no script request, no cookie, no consent
  banner, no console output. This is the normal state for local dev, docker-compose and the
  development GCP environment, so only production reports.
- Both ids live in the **`frontBuild`** block of `deploy/secrets.<env>.json`. `SpaceStroker.Deploy`
  already writes that block into `.env.production.local` in **both** repos and each toolchain only
  reads its own prefix, so no deploy-tool code is involved. Only `secrets.example.json` is tracked in
  git and it carries empty placeholders — a real id is never committed.

**Consent (Consent Mode v2)**
- `gtag` boots with `analytics_storage: 'denied'` **before** the tag loads, so the first hit can never
  write a cookie.
- A banner on both properties offers **Accept** and **Reject** with equal prominence. Accept issues a
  `consent update` to `granted`; Reject leaves storage denied. Neither answer is re-prompted.
- The answer is stored in an **`ss_consent`** cookie (`granted`/`denied`, 180 days, `SameSite=Lax`,
  `Secure` on https) scoped to **`.spacestroker.com`** — not `localStorage`, which `www.` and `app.`
  would not share. A choice made on the LP is therefore honoured by the app and the player is asked
  once. On any host that is not `*.spacestroker.com` the `domain` attribute is omitted, because a
  domain the browser rejects would silently write no cookie and re-prompt forever.
- A denied visitor still produces cookieless pings, so traffic volume and source survive without
  storage.

**What is measured**
- Automatic: pageviews, referrer, `utm_*`, coarse geo, device/browser, new vs returning.
- `cta_play_click` (LP) — every CTA that navigates to `GAME_URL`, with `cta` identifying which one
  (`hero`, `how_it_works`, `header`, `header_mobile`).
- `wallet_connect_attempt` / `_success` / `_failure` (app) — with `wallet_name` and `connect_path`
  (`desktop_extension` | `mobile_deeplink` | `wallet_in_app_browser`); failures carry the error
  **name** only.
- `bet_placed` (app) — a **count**: `game_type`, `payment_method`, `is_free`.
- `game_type_selected` (app), plus a manual `page_view` per SPA route.
- **Never sent:** stake or payout amounts, wallet addresses, launch-token claims, display names, chat
  content, or any other player identifier. The backend's `GET /games/stats` and the ledger remain the
  only authoritative source for money; GA4 is deliberately not a second, disagreeing set of figures.
- Bet **outcome** (won/lost) is deliberately not tracked.

**Where it must not run**
- **Embedded (iframe) sessions are fully excluded** — no script, no banner, no cookie — via the
  existing `useEmbed`. Partner-casino traffic would otherwise pollute our own acquisition data, and we
  must not set cookies inside a partner's page outside their consent flow.
- The `chromeless` routes `/space-stroker/wallet-bridge` and `/space-stroker/background` send no
  pageview.
- `monkify-front/public/verify.html` and `public/embed-example.html` never load the SPA bundle, so
  they are excluded by construction.
- `/error` **is** tracked — a spike there is a signal worth having.

**Robustness**
- Every call site is a safe no-op. If the script is blocked (an ad blocker, DNS filtering, an empty
  id) nothing throws, no navigation is delayed and no bet is gated. All four LP CTAs are
  `target="_blank"`, so a tracked click cannot race its own navigation.
- The SPA sends exactly one `page_view` per navigation (`send_page_view: false` at config time plus a
  `router.afterEach` hook), so the first load is not double-counted.
- The mobile wallet deeplink hop reloads the SPA on return and may emit one extra `page_view`. This
  is accepted, not worked around.
- `?ga_debug=1` sets `debug_mode`, putting the session in GA4 DebugView.

**Known limitation**
- GA4 is blocked by a meaningful share of a crypto-savvy audience — expect a 20–40% undercount. It is
  consistent over time, so trends and relative source mix stay valid. A first-party proxy to defeat
  blockers is explicitly out of scope.

## Key code touchpoints

**`monkify-front`**
- `src/hooks/useAnalytics.ts` — the **only** module that touches `gtag`. Owns enablement, script
  injection, Consent Mode defaults, the `ss_consent` cookie, `trackPageView`, and the typed event
  helpers. Anything needing analytics calls the composable; nothing else imports the tag.
- `src/main.ts` — `initAnalytics()` before mount, and the `router.afterEach` pageview hook that skips
  `meta.chromeless`.
- `src/components/ConsentBanner.vue` — mounted from `App.vue`; self-gating on
  `analyticsEnabled && !consentDecided`.
- `src/hooks/useGame.ts` → `submitBet` — `bet_placed`. This is the **single choke point** every bet
  path funnels through (free, Standard, SolanaToken desktop, the embedded popup handoff, and the
  resumed mobile-deeplink bet), so the event belongs there and nowhere else.
- `src/utils/wallet.ts` → `handleWalletError` — `wallet_connect_failure`. It already suppresses
  pre-interaction auto-connect noise via `userHasInteracted`, so the event inherits that filter.
- `src/hooks/useMobileWallet.ts` — deeplink attempt, and success/failure in `finalizeConnectReturn`;
  Tier-2 `openInWallet` reports the `wallet_in_app_browser` attempt.
- `src/App.vue` — desktop `connecting`/`connected` watchers (mobile excluded so a wallet-browser
  connect is not double-counted) and the in-wallet-browser success.
- `src/components/game-selection/GameTypes.vue` → `chooseGame` — `game_type_selected`.
- `.env.example` — documents `VUE_APP_GA_MEASUREMENT_ID`.

**`monkify-lp`**
- `src/layouts/Layout.astro` — the gtag boot, Consent Mode defaults, `window.ssTrack`, and the
  **delegated document click listener** for CTAs. Delegation is not a style choice: the `index.astro`
  buttons carry no `client:*` directive, so Astro ships them as static HTML with no Vue runtime and a
  per-component `@click` would silently never fire — while the hydrated `Header.vue` CTAs would work,
  which is exactly how that bug survives review.
- `src/pages/index.astro` (`data-cta="hero"`, `"how_it_works"`) and `src/components/Header.vue`
  (`"header"`, `"header_mobile"`) — the four and only links to `GAME_URL`.
- `src/components/ConsentBanner.vue` (`client:idle`), `src/pages/cookies.astro` (the public cookie
  notice, indexable and in the sitemap), `src/components/Footer.vue` (links to it).
- `@vercel/analytics` was **removed** — it had been inert since the LP moved to GCP, where its script
  404s.

**`monkify-meta`**
- `deploy/secrets.example.json` — both keys as empty placeholders, with the rationale inline.
- `marketing/utm-conventions.md` — the fixed `utm_source` vocabulary and ready-to-paste tagged links;
  `marketing/twitter-launch-campaign.md` and `telegram-launch-campaign.md` resolve `[PLAY_URL]` per
  placement.

## Operational notes (not code)

Two steps live in the GA4 console and cannot be done from the repo:

- The property + web data stream must exist, and **only one stream** — a second stream would break the
  single session across the LP→app hop.
- The custom event parameters (`cta`, `wallet_name`, `connect_path`, `game_type`, `payment_method`,
  `is_free`, `failure_reason`) must be **registered as event-scoped custom dimensions**. GA4 collects
  unregistered parameters but never shows them in reports, which is the usual reason instrumentation
  appears broken.
