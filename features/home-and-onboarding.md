# Domain — Home and onboarding

> **What this covers.** How a first-time visitor arrives, learns what the game is, and tries it
> without money: the home dashboard at `/`, the `/how-to-play` guide, `/responsible-gaming`, and the
> fully-offline `/game/practice` round.
>
> Deep design: [`../specs/home-dashboard.md`](../specs/home-dashboard.md) (+ the follow-ups
> [`home-hero-sprite`](../specs/home-dashboard.md), `home-hero-trim`, `home-orbit-card` and
> [`app-home-changes`](../specs/app-home-changes.md), whose small specs were retired into it) ·
> [`../specs/how-to-play.md`](../specs/how-to-play.md) +
> [`../specs/how-to-play-changes.md`](../specs/how-to-play-changes.md) ·
> [`../specs/practice-mode.md`](../specs/practice-mode.md) +
> [`../specs/practice-mode-changes.md`](../specs/practice-mode-changes.md).
>
> ⚠️ **`how-to-play-changes`, `practice-mode-changes` and `app-home-changes` ride the
> `release/ui-ux-changes` train and are NOT on `main` yet.**

## 1. The home dashboard (`/`)

The app has a front door. `/` renders a landing page — hero, the real game picker, the player's own
numbers, and panelled sections leading to every other surface — instead of redirecting into the bare
lobby.

Before this, `/space-stroker/game` *was* the app: a 16-line wrapper around three cards, with Stats,
History, Provably Fair, Settings and the tutorial reachable **only** from the ☰ menu — which a
first-time player has no reason to open.

⚠️ **The `/space-stroker` URL prefix is gone** (the app is hosted at `app.spacestroker.com`). That is
why the LP's play CTA targets the **bare origin** and not a route — see [`lp.md`](lp.md).

## 2. `/how-to-play`

One readable, linkable page explaining the game, in six deep-linkable sections: the game itself
(premise + getting in + the round), the payout, the live modes, provable fairness, refunds, and the
optional preferences.

It **replaced the Quick Tour**, which auto-opened once behind seven guards and was then gone forever.
⚠️ **Nothing auto-opens any more, on any route, for any player.** The Quick Tour is **deleted from the
product** — `QuickTourModal.vue`, `useQuickTour.ts`, `constants/quickTours.ts`, the seven guards and
the version keys are all gone.

⚠️ The guide deliberately **names no commission number** — the rate is rendered live from the backend
elsewhere, and a hardcoded number here would rot. See [`money.md`](money.md).

The same card converted **Responsible Gaming** from a ☰ modal into its own page at
`/responsible-gaming`. See [`legal.md`](legal.md).

## 3. `/game/practice`

A first-time visitor cannot see what Space Stroker is: watching one round end requires a connected
wallet, an on-chain USDC transfer **and** a second player — every seeded type is
`MinimumNumberOfPlayers: 2`.

`/game/practice` is a complete round that runs **entirely in the browser**: no request, no socket, no
wallet, no money. It is reached from the three Practice CTAs on `/` and ⚠️ **it works with the API
down**.

⚠️ **It is built to feel like the real game rather than to approximate it** — same cabinet, same status
copy, same timings, same animations, same audio bed. **If the real game screen changes, practice should
change with it.** Later work added a guided tutorial and an always-win outcome.

## Key code touchpoints (`monkify-front`)

`src/pages/HomePage.vue`, `src/pages/HowToPlayPage.vue`, `src/pages/ResponsibleGamingPage.vue`,
`src/pages/PracticePage.vue`, the guide section components (`GuideWhatYouWin.vue` and siblings),
`src/routes.ts` (prefix-free routes), and the home hero/sprite components.

**No backend surface** beyond what the home dashboard reads from the existing stats endpoints.
