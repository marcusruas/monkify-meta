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

**Section order:** hero → the mode picker → **the token card** → how-it-works → feature cards → the
player's own record → everyone's numbers. ⚠️ **The mode cards come first** because they are the only
thing on the page a visitor can act on, and ⚠️ **the token card is second, not under the hero** — it was
under the hero for one build and the owner reversed it, because a billboard must not push the actionable
thing below the fold. It renders nothing at all unless a mint is configured. See
[`token-launch.md`](token-launch.md).

## 2. `/how-to-play`

One readable, linkable page explaining the game, in deep-linkable sections: the game itself (premise +
getting in + the round), the payout, the live modes, provable fairness, refunds, **the project's coin**,
**the Feedbacker Program** and the optional preferences.

⚠️ **TWO sections are CONDITIONAL** — the token section and, since Trello #173, the **Feedbacker**
section ([`feedbacker.md`](feedbacker.md)). Each enters `GUIDE_SECTIONS` only on a build with a mint
configured, so both the nav and the body lose them together and the page is otherwise unchanged. Their
fragments still resolve safely without them (the page matches the hash against that same array). The
coin sits after refunds and Feedbacker directly after the coin: everything about the game comes first,
and ⚠️ **a manual that explains a tradeable asset before it has finished explaining the game is a
prospectus**. The coin section is also the surface that carries the **real supply and treasury figures**
the LP deliberately omits — the Feedbacker section is gated for a plainer reason: every reward it
describes is paid in the token, so without a mint it would advertise payouts in a currency that does not
exist.

⚠️ **`#feedbacker` is a CROSS-REPO URL contract.** The landing page links to
`/how-to-play#feedbacker` — the LP's first ever link into an app subpage — so renaming that id breaks a
link that lives in `monkify-lp`, where nothing in this repo would catch it.

It **replaced the Quick Tour**, which auto-opened once behind seven guards and was then gone forever.
⚠️ **Nothing auto-opens any more, on any route, for any player.** The Quick Tour is **deleted from the
product** — `QuickTourModal.vue`, `useQuickTour.ts`, `constants/quickTours.ts`, the seven guards and
the version keys are all gone.

⚠️ **The guide NOW NAMES THE RATE — 1% (owner, 2026-08-12).** It used to name no number, on the grounds
that the rate is configurable and a hardcoded figure would rot; what changed is that the LP and the token
card have always said *"the 1% house cut"*, which left the manual as the only surface refusing to. The
hedge survives as the sentence after it: the number is named **and** the player is still sent to the
STATS panel for their mode's live rate. ⚠️ **Two sections carry the figure** (`GuideWhatYouWin` and
`GuideToken`) — if a deployment ever runs a rate that is not 1%, **both are wrong and both move**. See
[`money.md`](money.md).

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

**The status copy is a verbatim duplicate, and it is duplicated on purpose** — `practiceStatusLabel`
(`src/constants/practiceGame.ts`) and `GameTerminal`'s `gameStatusLabel` hold the **same strings, word
for word**, copied rather than shared (`specs/practice-mode.md` §7.3). **Change one, change the other.**
A defect found in practice is therefore usually a defect on the money screen too: the tutorial's beat 2
holds the round *after* EDSON-BOT's bet lands, so `remaining` is already `0` and both maps printed
`> YOUR BET IS IN! NEED 0 MORE PLAYERS...` for as long as the bubble was up. Both now guard
`remaining <= 0` → **`> ALL BETS IN. STAND BY...`**, and ⚠️ the guard sits **outside** the
`userHasBet` ternary — the other arm would otherwise read `> NEED 0 MORE PLAYERS TO START...`, the
same defect wearing a different label. Both of `GameTerminal`'s arms (race **and** lottery) carry it.

**On a phone the tutorial bubble owns its own stacking tier and Edson sinks out of its way** — the
geometry is load-bearing and easy to get wrong (a flat sink drew the bubble on his face twice). See
[`frontend-ux.md`](frontend-ux.md) §3 → "The practice tutorial bubble below `lg`".

## Key code touchpoints (`monkify-front`)

`src/pages/HomePage.vue`, `src/pages/HowToPlayPage.vue`, `src/pages/ResponsibleGamingPage.vue`,
`src/pages/PracticePage.vue`, the guide section components (`GuideWhatYouWin.vue` and siblings) +
`src/constants/guideSections.ts` (the registry both the nav and the body derive from),
`src/routes.ts` (prefix-free routes), and the home hero/sprite components.

⚠️ **`HomeSection.vue`'s `title` prop is optional ONLY because of its `header` slot.** A section taking
the default header still must pass one — an untitled panel is a bug, not a variant. The slot exists for
the single panel whose heading *is* a piece of type (the token card's ticker), it replaces the whole head
block, and nothing supplies a fallback `<h2>`.

**No backend surface** beyond what the home dashboard reads from the existing stats endpoints.
