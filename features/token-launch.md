# Domain — The launch token (the project's coin)

> **What this covers.** The **pump.fun** coin the project launches, and the four places it is presented
> to a reader: the landing page's `#token` section, the app's home card, the app's `/how-to-play` token
> section, and the token row in the app's ☰ menu pad.
>
> **Frontend-only — there is NO backend surface.** No endpoint, DTO, entity, migration, consumer or
> SignalR contract exists for this coin, and none of it touches the bet or settlement path. It was built
> **ahead of the launch** so going live is a config value plus a redeploy, not a code drop.
>
> Deep design: [`../specs/token-launch-ui.md`](../specs/token-launch-ui.md).

---

## 1. ⚠️ The launch token is NOT the settlement token

This is the single rule everything else here depends on.

| | Settlement mint | Launch token |
| --- | --- | --- |
| What it is | The one token every game pays out in — **USDC** | The project's **pump.fun** coin |
| Where it lives | Backend `GeneralSettings:Operator:Token:MintAddress`; frontend `VUE_APP_GAME_TOKEN_ADDRESS` | `VUE_APP_TOKEN_MINT` (app) / `PUBLIC_TOKEN_MINT` (LP) |
| What it does | Moves real money — stakes, payouts, refunds, the ledger | **Nothing.** It is information on a page |

**Rounds settle in USDC and the launch token is information only.** It is not a stake, not a balance,
not a payment method. ⚠️ **Never reuse `VUE_APP_GAME_TOKEN_ADDRESS` for a token surface, and never point
a token surface's env key at settlement** — the two are independent values on purpose, and conflating
them means changing one silently moves the other. See [`money.md`](money.md) for the money path this
does not touch.

## 2. The two env keys — one switch, one address

Everything is derived from **two** values, both written by the deploy tool's existing `frontBuild` block
into **both** repo builds (each toolchain only exposes its own prefix):

| Key (app / LP) | What it gates |
| --- | --- |
| `VUE_APP_TOKEN_MINT` / `PUBLIC_TOKEN_MINT` | **The switch.** Non-empty ⇒ every token surface exists. Empty ⇒ none of them do. |
| `VUE_APP_TREASURY_WALLET` / `PUBLIC_TREASURY_WALLET` | Only the copyable **treasury address bar**. Everything else in the treasury block still renders. |

- **Both are FAIL-CLOSED**, and against a real test — `/^[1-9A-HJ-NP-Za-km-z]{32,44}$/`, not a truthiness
  check. Empty, whitespace or anything that is not plausible base58 is treated as unset, so a Buy button
  can never point at nothing and neither site can publish a half-typed wallet. Same direction as
  `VUE_APP_ORBIT_ENABLED`; see [`../conventions/frontend.md`](../conventions/frontend.md) → Feature flags.
- **The two are independent.** A build may legitimately have a live mint and no treasury wallet.
- ⚠️ **They must hold the SAME values in both prefixes.** Two sites publishing two different treasuries
  is worse than neither publishing one.
- ⚠️ **Both sites are STATIC builds** (Vue CLI and Astro bake env at compile time), so going live is a
  **rebuild + redeploy of two sites**, not a runtime toggle. The coin is live on-chain the instant it is
  minted; the sites only catch up when the pipeline finishes, and **launch day must deploy both or they
  disagree**.
- The committed config (`deploy/secrets.example.json`, `monkify-front/.env.example`) carries **empty**
  values only. The real mint belongs in `secrets.production.json`, which is never committed.
- ⚠️ **No mint is hardcoded anywhere in either repo.** Every URL is derived: `pump.fun/coin/{mint}`,
  `dexscreener.com/solana/{mint}`, `solscan.io/account/{treasury}`.

## 3. Pre-launch is a real state, not an absence

With no mint configured, **the app is byte-identical to the pre-#160 app** — no home card, no guide
section, no menu row — and **the LP keeps its "No token. Yet." panel**, anti-scam line intact, now
sitting in the page's **second** slot.

⚠️ **That panel is deliberately given the prime slot pre-launch.** The window before a real coin exists
is exactly when fake ones appear, so the warning is most valuable where it is most visible. The
knowingly-accepted cost is that the most prominent section on the page says "there is nothing to buy".

## 4. What the copy is allowed to claim

- ⚠️ **Both named mechanics are FUTURE TENSE, always.** The copy promises that the house **will** buy and
  burn from the existing 1% cut, and that Space Jackpot winners **will be rewarded** with the token from
  the treasury. **Neither exists in code.** Present tense fails an on-chain check on day one. Each
  mechanic is its own follow-up card.
- **The jackpot mechanic's wording (owner, 2026-08-12).** The heading is **`Space Jackpot earns you
  $STROKE`** and the body **`Every winner of Space Jackpot will be rewarded with $STROKE from the
  Treasury Wallet.`** It replaced *"Bigger jackpots"* / *"The daily Space Jackpot will carry a $STROKE
  bonus on top of the USDC pot"* — the heading names the mode **and** the payout, so the body says who
  gets it and where it comes from instead of repeating the mode name.
  ⚠️ **That pair is verbatim in TWO files and they move together:** `monkify-lp` →
  `TokenSection.vue` and `monkify-front` → `HomeTokenCard.vue`. Changing one ships two different
  promises. ⚠️ **The GUIDE's jackpot row is deliberately NOT synced to it** — `GuideToken.vue` keeps
  *"No fixed size, no schedule: it is a bonus, not a payout rule"*, which is the owner's call that the
  manual must not put a figure or a schedule on a discretionary bonus. The two are compatible (every
  winner gets some; the amount is never promised); do not "fix" the difference.
- ⚠️ **Nothing may state or imply the token can be used to place bets.** This is why
  `SpaceRoadmap.vue` phase 5 no longer reads *"the token stays your stake in all of it"* — vague future
  talk before launch, a checkable lie once a real contract address sits two sections above it. (Phase 1's
  *"The token drops on Solana"* is fine and was left alone.)
- **The ticker is `$STROKE`, a stand-in.** It is a single constant in each repo, marked `TODO(launch)`,
  because it is not yet decided.
- **The treasury is disclosed, not hidden** — ~50% of supply, framed as the fuel for the two mechanics.
  DexScreener/Bubblemaps/RugCheck surface a position that size within minutes, so silence would read as
  concealment; framing it as the point of the section is what turns the number from a red flag into the
  pitch.

## 5. Where it appears

### Landing page (`monkify-lp`)

`#token` moved from position **6 to 2** — `hero → #token → #about → #modes → #story → #roadmap →
#community` — and `Token` became the **first** nav item (desktop + mobile, gated). The live section
carries: the coin, the ticker, a copyable contract bar, **exactly two CTAs** (Buy on pump.fun, Chart),
the two mechanic cards, and the treasury block. The hero's secondary CTA becomes `Buy $STROKE` and three
of the hero HUD's four corners become real controls.

⚠️ **It deliberately does NOT display total supply, decimals or mint authority.** They are free facts on
any pump.fun launch and read as padding. See §6 for the one place that does.

⚠️ **`ContentSection`'s backdrop props are POSITIONAL.** `hue` / `neb` / `pattern` / `nebOpacity` run a
one-way blue→red journey down the page and **stay with the slot**; only `art` travels with its section.
Moving a section without rebalancing those puts two similar backdrops next to each other. See
[`lp.md`](lp.md).

### The app (`monkify-front`)

| Surface | Where | Notes |
| --- | --- | --- |
| `HomeTokenCard.vue` | `/`, **second** section — after the mode picker, before "how it works" | Coin + contract bar left, heading + copy + CTAs right |
| `GuideToken.vue` | `/how-to-play`, **second-to-last** section | The surface that states the real figures |
| The token row | Foot of the ☰ menu pad | Opens **pump.fun in a new tab** |

- ⚠️ **The home card is SECOND, not under the hero — and it was under the hero for exactly one build.**
  Refinement had put it first on the grounds that the coin is the campaign for this window; the owner
  reversed that on 2026-08-11 after seeing it rendered. Pushing the mode cards below the fold buys a
  **billboard** a first impression at the cost of the one thing a visitor came to do. *"The launch is the
  priority"* is the argument that was already heard and lost — do not re-promote it without the owner.
- ⚠️ **There is NO token button on the header bar.** It shipped there as a permanent amber pill for one
  build and was pulled (owner, 2026-08-11): the bar is the funding/round strip (wallet + balance +
  audio), and an always-on outbound promo competed with the in-game primary actions on **every** screen,
  mid-round included. Its replacement is a **full-width strip at the foot of the ☰ pad** — not a tenth
  grid cell, which would start a fourth row the pad can never fill, and which would promise a page when
  this is the only destination in the pad that **leaves the app**.
- ⚠️ **New tab is load-bearing.** A player mid-round keeps their route, their game state and their
  SignalR connection. Never `router.push`, never same-tab.
- **Every surface also hides when embedded** (`useEmbed().isEmbedded`) — see
  [`embedding.md`](embedding.md).

## 6. ⚠️ The app manual says MORE than the LP, on purpose

`/how-to-play`'s token section carries the **1,000,000,000 total**, the mint address, the treasury
address, the **1% cut by name** and the **team release ladder**. The LP carries none of those.

This is the agreed split (owner, 2026-08-12), not drift: the manual is the surface that states real
figures, and every claim in it lands on a number or an address a reader can paste into an explorer. **Do
not "harmonise" the two by deleting from the guide.**

### ⚠️ Percentages only — no token counts anywhere in the copy

Owner, 2026-08-12: *"don't talk in token units, only in percentages."* This **reverses** the rule that
"real figures" meant resolved counts. Every share on every surface is a **percentage of supply**:

| Was | Is |
| --- | --- |
| `250,000,000 IS SET ASIDE FOR PLAYERS` | `25% OF THE SUPPLY IS SET ASIDE FOR PLAYERS` |
| legend note opening `250,000,000 $STROKE — …` | the note alone (the `dt` above it already carries `25%`) |
| team eyebrow `THE TEAM'S 100,000,000` | `THE TEAM'S 10%` |
| *"We take it 10,000,000 at a time"* | *"We take it 1% at a time"* |
| stepper gains `+10M` / `all 100M` | `+1%` / `all 10%` |

**Why:** a count is only as good as `TOTAL_SUPPLY`, which is an assumption about the launchpad, and the
percentages themselves are still `TODO(launch)` at their source. A share survives both of those moving;
a hard count silently becomes a false statement.

⚠️ **`tokensForPercent` and `UNLOCK_STEP_TOKENS` were DELETED from
`monkify-front/src/constants/token.ts`**, not left unused — re-adding a count is now a visible change to
that file rather than a one-line slip in a template.

⚠️ **THE ONE EXCEPTION is the `1,000,000,000` total** (the guide's lede and its supply eyebrow). It is
the denominator every percentage is read against, it cannot be restated as a percentage, and it is the
app-only disclosure this whole section exists to protect. `formatTokens` survives for exactly that.

#### ⚠️ THE SECOND EXCEPTION — the Feedbacker Program states COUNTS, on purpose (2026-08-17)

The whole of [`feedbacker.md`](feedbacker.md) — the LP's `#feedbacker` section and the app guide's
`#feedbacker` section — is **exempt from the rule above** and states plain token counts: `250M set
aside`, the `60M / 50M / 40M / 30M / 20M` season bars, the `100k / 300k / 600k` bug tiers, `up to
500k`, `up to 50k`, and the `2.5M` season cap. Note the first row of the table above is **literally
that section's headline**, so this looks exactly like the mistake the rule exists to prevent. It is
not. **Do not "fix" it back.**

Owner's call at refinement (Trello #173), after being shown both alternatives and rejecting each:

| Rejected | Why it lost |
| --- | --- |
| The design's own **figures-free** turn (`v5`/`5b`: a `SMALL / MEDIUM / LARGE` word scale, chart deleted) | Reviewed and rejected; the owner wanted the numbers |
| **Percentages of supply** | The season bars convert cleanly (`6/5/4/3/2%`) but the per-submission prices collapse to `0.01% / 0.03% / 0.06% / 0.05% / 0.005%` — three near-identical strings differing in the second decimal, in a pixel font |

⚠️ **The exemption is SCOPED to that programme and changes nothing else.** `TokenSection.vue`,
`HomeTokenCard.vue` and `GuideToken.vue` keep their percentages; **`tokensForPercent` stays deleted**
and `formatTokens` is **not** reintroduced. Every Feedbacker figure is a hand-written display string in
the mirrored `FEEDBACKER` blocks — nothing there derives from `TOTAL_SUPPLY`, so the "a count is only
as good as `TOTAL_SUPPLY`" objection does not bite: the counts are the source, not a derivation.

⚠️ **That programme's budget IS the 25% `Community & game rewards` slice**, not a second pot, and its
section says so in one line — the two sit two slots apart on the LP, one saying `250M` and one saying
`25%` about the same money. See [`feedbacker.md`](feedbacker.md), which also records an **open
arithmetic conflict between those season budgets and the treasury's Streamflow lock** that blocks the
next LP deploy.

> ⚠️ **KB GAP, noticed 2026-08-17 and NOT closed by #173.** The **Streamflow treasury lock** —
> `VEST_PERCENT = 49`, `VEST_WEEKS = 52`, contract `Ar3XwZ…i16U`, starting 2026-08-14 and running to
> `Aug 13, 2027` — shipped to `monkify-lp` `master` (`1a23f1a`, the LP token section) and to
> `monkify-front` `main` (`0bb2b6c`, `GuideToken.vue` + `constants/token.ts`) **without a knowledge-base
> sync**, so this document does not describe it and the surfaces it changed are stale here. It is
> summarised in [`feedbacker.md`](feedbacker.md) only as far as #173's own open conflict needs. Someone
> should document it properly — that is another card's job, not this one's.

### ⚠️ No em dashes in the guide's token copy

Owner, 2026-08-12: *"remove em dashes from the how-to-play#token (ai giveaway)."* Every `—` in the
**rendered** text of `GuideToken.vue` is now a comma, a colon or a full stop; the ones left in the file
are all inside code comments. It is a **punctuation** rule, not a rewrite licence — the sentences are the
owner's own and were only re-punctuated. ⚠️ `–` and ` - ` are **not** substitutes; same tell, different
glyph.

> ⚠️ **Scoped to `/how-to-play#token`.** The LP's own token section (including its `STRAIGHT TALK`
> pre-launch branch, which is marked final copy) still uses em dashes and was **not** swept. Widening
> this to the rest of either site is a separate decision.

⚠️ **The consequence: `TREASURY_SLICES` now exists in BOTH repos** (`monkify-lp/src/constants.ts` and
`monkify-front/src/constants/token.ts`). **A percentage change is a two-repo change.** The underlying
numbers are still `TODO(launch)` at both sources — only the 10% team slice came from the brief.

## 7. Analytics

One event, `token_click`, carrying a **`source` label and nothing else** — no wallet, no amount, no mint.
Values: `home_buy`, `home_read_more`, `menu`, `guide_buy`, `guide_supply`.

⚠️ **Deliberately NOT folded into `home_link_click`.** That series' documented meaning is *"which surface
do players reach for from the front door"*; this one counts *"how many people want to know about the
coin"*, wherever it is shown. `home_read_more` routes to `/how-to-play#token` (internal, since
2026-08-12) and `guide_supply` opens the DexScreener chart — so the three read as a **funnel**:
front-door card → guide → chart. See [`stats-and-history.md`](stats-and-history.md) → Analytics.

## 8. Open before the LIVE build

None of these block the code — every one is a single source constant — but all must be settled before a
build with a real mint ships:

1. **The ticker** (`$STROKE` is a stand-in, `TODO(launch)` in both repos).
2. **The treasury percentages** — two of the three slices are placeholders sized to land the treasury on
   50%; ⚠️ the bar renders `percent` as a raw flex weight and does **not** normalise, so slices that do
   not total 100 with the public float silently misrepresent the split.
3. **Confirmation that buyback-and-burn and jackpot seeding are genuinely intended** — naming them in
   copy makes them a public promise.
4. Whether the apex `spacestroker.com` or `www.` is canonical for any LP deep link.

## Key code touchpoints

**`monkify-lp`** (on `master`)

| File | Role |
| --- | --- |
| `src/constants.ts` | `TOKEN_MINT` / `TOKEN_LIVE` / `TREASURY_WALLET` (the base58 gate), `TOKEN_TICKER`, `TREASURY_SLICES` + derived totals, the pump.fun / DexScreener / Solscan URLs, `shortMint`, and `SOCIALS` |
| `src/components/TokenSection.vue` | Both states — the live section and the "No token. Yet." panel |
| `src/pages/index.astro` | `#token` in slot 2, the rebalanced `ContentSection` props, the gated hero CTA |
| `src/components/Header.vue` | `Token` as the first nav item, gated |
| `src/components/HeroHud.vue` | The interactive hero corners (pump.fun status, socials, contract) — ⚠️ **not** `aria-hidden`, unlike the decorative HUD it was split out of |
| `src/components/SpaceRoadmap.vue` | Phase 5's de-staked copy |
| `src/components/PixelText.vue` · `PumpMark.vue` · `PixelIcon.vue` · `src/icons.ts` | Display-face glyph corrections and drawn marks |

**`monkify-front`**

| File | Role |
| --- | --- |
| `src/constants/token.ts` | `tokenLive`, `TOKEN_TICKER`, the URLs, the mirrored `TREASURY_SLICES` |
| `src/components/home/HomeTokenCard.vue` | The front-door card |
| `src/components/guide/sections/GuideToken.vue` + `src/constants/guideSections.ts` | The conditional manual section (⚠️ `#token` still resolves safely on a build without it) |
| `src/components/GameHeader.vue` | The ☰ pad's token row |
| `src/hooks/useAnalytics.ts` | `trackTokenClick` |
| `src/components/home/HomeSection.vue` | The `header` slot that made `title` optional, for the one panel whose heading *is* the ticker |
| `src/components/ui/PixelText.vue` · `PumpMark.vue` · `MoneyValue.vue` | The ported `$` correction |

**`monkify-meta`** — `deploy/secrets.example.json` + `deploy/README.md` (`frontBuild`).

## Known gaps

- **No browser QA was run** in either env state before shipping (owner waived it, 2026-08-12); both
  builds are clean and the empty-mint state is the current production behavior, but the **live-mint state
  has never been rendered in a browser**.
- `monkify-backend/scripts/setup_local_environment.bat` does **not** emit the two keys, so the local
  Docker stack can only exercise the pre-launch state.
