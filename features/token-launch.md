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
| What it is | The asset every game pays out in — **native SOL** | The project's **pump.fun** coin |
| Where it lives | Backend `GeneralSettings:Operator:Token:MintAddress`; frontend `VUE_APP_GAME_TOKEN_ADDRESS` | `VUE_APP_TOKEN_MINT` (app) / `PUBLIC_TOKEN_MINT` (LP) |
| What it does | Moves real money — stakes, payouts, refunds, the ledger | **Nothing.** It is information on a page |

**Rounds settle in SOL and the launch token is information only.** It is not a stake, not a balance,
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
  the treasury. Present tense fails an on-chain check on day one. Each mechanic is its own follow-up
  card.
  ⚠️ **HALF OF THAT IS NOW STALE (2026-09-16).** The **jackpot mechanic SHIPPED** — every winner of a
  Space Jackpot round is paid **100,000 $STROKE** by `JackpotRewardService`
  ([`../specs/jackpot-stroke-reward.md`](../specs/jackpot-stroke-reward.md)). The copy's future tense
  is now merely conservative rather than required. **Buyback-and-burn still does not exist in code**
  and its future tense is still load-bearing.
  ⚠️ **The copy says "from the Treasury Wallet" and the chain says otherwise.** The bonus is signed by
  the **house settlement wallet** `CjAULxZV5…`, because the disclosed treasury `GqxFusRS…` is inside
  the Streamflow lock (§5) and cannot sign ad-hoc transfers. Anyone checking Solscan sees a wallet
  neither surface names. Unresolved — either the copy moves or the treasury funds the house visibly.
- **The jackpot mechanic's wording (owner, 2026-08-12).** The heading is **`Space Jackpot earns you
  $STROKE`** and the body **`Every winner of Space Jackpot will be rewarded with $STROKE from the
  Treasury Wallet.`** It replaced *"Bigger jackpots"* / *"The daily Space Jackpot will carry a $STROKE
  bonus on top of the SOL pot"* — the heading names the mode **and** the payout, so the body says who
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
  ⚠️ **DELIBERATELY REVERSED FOR SPACE JACKPOT (2026-10-01, card #196).** The token still never
  **stakes** a bet — no mode takes $STROKE as a stake — but **holding it grants a free Space Jackpot
  entry**, and the copy now says so on purpose: **"Token holders get a free daily lottery draw."** Do
  **not** "fix" that copy back to this rule. See
  [`space-jackpot-refactor.md`](space-jackpot-refactor.md).
- ⚠️ **THE JACKPOT BULLETS ABOVE ARE HISTORY SINCE 2026-10-01.** The 100,000 $STROKE winner bonus is
  **removed** (`JackpotRewardService` deleted), so nothing is paid "from the Treasury Wallet" any more
  and the signer mismatch noted above no longer exists. The heading is now **`Holders play Space Jackpot
  free`** and the body **`Token holders get a free daily lottery draw. Hold the day's minimum $STROKE at
  the draw.`** — still verbatim in `TokenSection.vue` (LP) and `HomeTokenCard.vue` (app), still moved
  together. `GuideToken.vue`'s row is now **`FREE JACKPOT FOR HOLDERS`** and states the rule (hold the
  minimum at entry and at the 10PM UTC draw, or the ticket is voided). Present tense is correct here:
  the mechanic runs. **Buyback-and-burn still does not exist in code.**
- **The ticker is `$STROKE`, a stand-in.** It is a single constant in each repo, marked `TODO(launch)`,
  because it is not yet decided.
- **The treasury is disclosed, not hidden** — ~50% of supply, framed as the fuel for the two mechanics.
  DexScreener/Bubblemaps/RugCheck surface a position that size within minutes, so silence would read as
  concealment; framing it as the point of the section is what turns the number from a red flag into the
  pitch. **Since 2026-08-14 it is also *locked* — see §5**, which is the strongest claim either surface
  makes and the only one here that is chain-enforced.
- ⚠️ **"The treasury" is 50% and "everything we hold" is 60% — since 2026-08-31 those are two different
  numbers.** The team's 10% left the treasury for its own wallet; see §5.1.

## 5. ⚠️ The treasury is LOCKED — and this is newer than most of this file

On **2026-08-14** the treasury went into a single **Streamflow** vesting contract
(`Ar3XwZpR3eY2ptz9nPbwUzzSybKCT8641ykSHWi6i16U`, *"Space Stroker Locked Treasury"*). Streamflow reports
it as **immutable** — *"can neither be canceled nor transferred"* — and **sender = recipient = the
treasury wallet both sites already publish** (`GqxF…qjme`).

| | |
| --- | --- |
| Held | **490M of the 1,000,000,000 supply — 49%**, not 50% |
| Type | **Linear**, unlock rate **Weekly**: one fifty-second per week |
| Window | **2026-08-14 17:18 GMT-3 → 2027-08-13**, exactly **52** weeks |
| Who can cancel / transfer | **No one** |

- ⚠️ **WHAT THE LOCK COVERS CHANGED ON 2026-08-31, THOUGH THE 490M DID NOT.** It holds the **treasury**
  — community and marketing. It does **not** cover the team's 10%, which now sits in its own wallet
  (§5.1). Every sentence built on this block used to say the lock covered *"the whole treasury, the
  team's slice included"*; **that sentence is now false and must not come back** — a Streamflow lookup
  disproves it in one click. The 490M is unchanged because it was always the treasury's tokens.
- ⚠️ **49%, NOT 50%, and both numbers stay.** The treasury lede still reads `~50% of the supply sits in
  the game treasury` and `TREASURY_SLICES` still sums to 50. 490M of 1B is 49%, and the ~1% difference
  is the part that was never streamed. The lock's figure is the one sitting directly under a *"go verify
  it"* link, so it is stated exactly rather than rounded up to match the prose beside it. ⚠️ It is a
  share **of total supply, not of the treasury** — 49 of the treasury's 50, never 98%.
- ⚠️ **THE LOCK AND THE TEAM'S MARKET-CAP LADDER ARE TWO DIFFERENT RULES OVER TWO DIFFERENT SCOPES.**
  Merging them is the mistake this section exists to prevent — and since the split they no longer even
  overlap:
  - the **weekly drip is chain-enforced** and covers the treasury: community and marketing, nothing else;
  - the **ladder** (1% per $100M step) is a rule the team set **itself** over its own 10%, which now
    sits **outside this contract** in its own wallet. **Nothing on-chain enforces the steps** — and that
    caveat now carries more weight, not less: before the split the tokens were at least held by the
    contract, and now they are in a plain wallet the team can move.
- ⚠️ **The owner's 2026-08-12 line — *"this thing is not LITERALLY locked btw, it's our suggestion of
  what we wanna do"* — PREDATES the contract by two days and now survives ONLY as a statement about the
  ladder.** It is still quoted at `UNLOCK_STEP_PERCENT` and in `GuideToken.vue`, scoped there. **Do not
  quote it against the lock block**, and do not restore the guide caveat's old opener `No vesting
  contract and no lock` — that sentence became false on 2026-08-14 and had the page denying a contract
  it links to two blocks above. It now reads *"The contract locks the tokens, not these steps: that part
  is a rule we set and a wallet you can watch."*
- **The contract ID is SOURCE in both repos, not a third env key.** Unlike the mint and the treasury
  wallet it can never differ between environments, so an env key would buy nothing and cost a
  deploy-tool change. It still runs through the same `ADDRESS_PATTERN`, so the block fails closed.
- **The week counter is derived, never written.** `vestedWeeks(now)` floors `(now − start) / 1 week`,
  clamped to `0…52`. ⚠️ **Stepped, never continuous** — Streamflow releases this contract in weekly
  increments, so a by-the-second reading would put tokens on the page that are still locked on-chain.
- ⚠️ **On the LP the counter renders only after the visitor's clock is read** (a `ready` gate plus a
  reserved-height box, so nothing shifts on hydration). Astro server-renders that island and Vue only
  repairs a mismatched text node in **dev** builds: seeding at build time would freeze the week number
  into a static file, and seeding at the contract's start would ship `100% still locked` long after that
  stopped being true — understating what has been released, in our own favour, on the one block that
  invites the reader to check. **The app needs no such gate** (client-rendered SPA, no server pass).

## 5.1. ⚠️ The team's 10% left the treasury (2026-08-31)

The team's slice used to be the third entry in `TREASURY_SLICES`, which made *"the treasury"* and
*"everything we hold"* the same 50%. They are now two different numbers, and **the whole token surface
depends on that distinction**:

| Constant | Share | What it is |
| --- | --- | --- |
| `TREASURY_TOTAL` | **50%** | The game treasury — community 30 + marketing 20. **The only thing the Streamflow lock covers.** |
| `TEAM_PERCENT` | **10%** | The team's, in its own wallet `2NBsPWJ5UGHnQ99aZJ8osge3HqXEDrVb2Zn99DV5rdGM`. **No contract covers it.** |
| `HELD_TOTAL` | **60%** | Everything the project holds. The coloured run of the supply bar. |
| `PUBLIC_FLOAT_PERCENT` | **40%** | The remainder. ⚠️ **Was 50%** — see the trap below. |

- **Community and marketing grew 25/15 → 30/20 because of this.** The treasury is still 50% of supply;
  that 50% now buys only those two things. The owner's framing: *"the old 50% now does not contain our
  dev supply. so its like 60 → 50 locked + 10 dev team on new wallet."*
- ⚠️ **THE PUBLIC FLOAT DROPPED FROM 50% TO 40%, AND THAT IS THE REAL COST OF THE SPLIT.** Every
  *"the other half hit the open curve"* sentence became false in the same change. Both repos rendered
  one; both now render `PUBLIC_FLOAT_PERCENT` instead of the word *half*. **Grep for `half` before
  claiming the float anywhere.**
- ⚠️ **`TEAM_SLICE` IS NOT IN `TREASURY_SLICES` AND MUST NOT BE PUT BACK.** The lock block derives its
  scope from that array, so an entry there silently returns the team's tokens to a *"nobody can move
  this"* claim that is no longer true. The bar and the table iterate **`SUPPLY_SLICES`**
  (`[...TREASURY_SLICES, TEAM_SLICE]`) so the 10% is still drawn and still named.
- ⚠️ **THE `percentFor('Team')` TRAP — it bit during this change and would have shipped silently.**
  `monkify-front`'s `percentFor` matches slices **by label**, so once `Team` left `TREASURY_SLICES` the
  lookup kept compiling while resolving to its `?? 0` fallback — printing *"the team's 0%"* across the
  guide with no error anywhere. `TEAM_PERCENT` now reads `TEAM_SLICE.percent` directly. **Any
  label-matched lookup is a silent-zero waiting to happen when a slice moves.**
- **The wallet address is SOURCE in both repos, not a fourth env key** — the same call already made for
  `VESTING_CONTRACT`: one fixed mainnet address that cannot differ between environments, so an env key
  would buy nothing and cost a deploy-tool change. It runs through `ADDRESS_PATTERN`, so a mistyped edit
  drops the address bar rather than shipping a link to nowhere.
- **Both surfaces publish the address**, because *"held separately"* is a claim and the address is the
  only thing that makes it checkable: the LP gets a third `token-cartridge` (magenta, under the
  treasury's) and the guide gets a `TEAM` `addr-bar` under the ladder caveat — which is also what
  finally makes that caveat's *"a wallet you can watch"* literal rather than a figure of speech.
- ⚠️ **THE WALLET WAS EMPTY WHEN THIS SHIPPED.** At the time of the change
  `2NBsPWJ5…rdGM` held **0 SOL and had no token account for the mint at all** (checked against mainnet
  RPC). Both sites link it straight to Solscan under a *"go verify it"* framing, so **the 100M has to
  land before this copy is deployed** or the most checkable claim on the page checks out as nothing.

## 6. Where it appears

### Landing page (`monkify-lp`)

`#token` moved from position **6 to 2** — `hero → #token → #about → #modes → #story → #roadmap →
#community` — and `Token` became the **first** nav item (desktop + mobile, gated). The live section
carries: the coin, the ticker, a copyable contract bar, **exactly two CTAs** (Buy on pump.fun, Chart),
the two mechanic cards, and the treasury block. The hero's secondary CTA becomes `Buy $STROKE` and three
of the hero HUD's four corners become real controls.

**The treasury block is THREE labelled beats, not one** (2026-08-17): `WHAT WE HOLD AS TREASURY` (lede)
→ `NOBODY CAN OPEN THE VAULT` (the lock, §5) → `WHERE IT GOES` (credits meter + supply table).
⚠️ **That third eyebrow is load-bearing, not decoration** — the split used to sit directly under the
lede, which *was* its heading; with a whole sub-block interposed it would otherwise read as a
continuation of the vesting contract rather than as the breakdown of what the treasury is for.
The lock beat carries a **52-cell week meter** (the same `.credit-meter` overlay trick re-cut from
twenty 5% cells, with a **1px hairline** divider rather than a percentage one — a 1/52 cell is ~6px on a
phone and a proportional divider would eat a sixth of it), whose **lit portion is what is STILL LOCKED**
and drains left to right, plus a **third `token-cartridge`** holding the contract.
⚠️ **That bar is labelled `VESTING`, not `STREAMFLOW`, and links `Verify ↗`, not `Streamflow ↗`** — at
360px the longer label squeezed `truncate` down to `Ar3…` and ate the address tail, which is the half a
reader eyeball-matches. The service is named in the sentence directly above it instead.
The hero chip rail also gains a **`49% LOCKED`** chip beside `LIVE ON PUMP.FUN`, ⚠️ **hidden below `sm`**
(the row is `flex` with no wrap and two stencil chips do not fit a 360px phone).

⚠️ **It deliberately does NOT display total supply, decimals or mint authority.** They are free facts on
any pump.fun launch and read as padding. See §7 for the one place that does.

⚠️ **`ContentSection`'s backdrop props are POSITIONAL.** `hue` / `neb` / `pattern` / `nebOpacity` run a
one-way blue→red journey down the page and **stay with the slot**; only `art` travels with its section.
Moving a section without rebalancing those puts two similar backdrops next to each other. See
[`lp.md`](lp.md).

### The app (`monkify-front`)

| Surface | Where | Notes |
| --- | --- | --- |
| `HomeTokenCard.vue` (`id="swap"`) | `/`, **second** section — after the mode picker, before "how it works" | Since #212: copy + contract bar + mechanics on the left, the **Jupiter swap** in a compact right column (behind a GET key on a narrow card); pump.fun is a small secondary `Also on pump.fun ↗` link. See [`token-integration.md`](token-integration.md) |
| `GuideToken.vue` | `/how-to-play`, **second-to-last** section | The surface that states the real figures; its buy button reads `Get $STROKE` and goes to `/#swap` |
| The token row | Foot of the ☰ menu pad | Goes to **`/#swap`, same tab** (since #212; was pump.fun in a new tab) |

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
- ⚠️ **REVERSED for buy links (owner, 2026-10-26, #212): every Buy / GET $STROKE link goes to `/#swap`
  same-tab** via `useSwapTarget().goToSwap(source)`, which scrolls to and highlights the Home swap card
  (already on Home → no navigation, it bumps a focus tick so a second click still scrolls). Surfaces: the ☰
  token row, GameTerminal and RaceScreen `GET $STROKE →`, GuideToken, the HomeCommunity pump.fun tile, the
  icon picker, the jackpot notice and the profile popup `$STROKE` row. Leaving a game page mid-round is
  accepted (placed bets settle server-side). The old rule ("new tab is load-bearing, never `router.push`")
  now applies **only to the non-buy external links**: Chart ↗, every Solscan ↗, the Streamflow vesting link,
  the swap card's `Swap on Jupiter ↗` fallback and `Also on pump.fun ↗` stay **new tab**.
- **Every surface also hides when embedded** (`useEmbed().isEmbedded`) — see
  [`embedding.md`](embedding.md).

## 7. ⚠️ The app manual says MORE than the LP, on purpose

`/how-to-play`'s token section carries the **1,000,000,000 total**, the mint address, the treasury
address, the **1% cut by name** and the **team release ladder**. The LP carries none of those.

Since 2026-08-17 it also carries the **lock** (§5), as its own `The lock: 49% of the supply` block
**between the supply split and the team ladder** — and the order is the argument: *what the project
holds → the contract that stops it moving → the extra rule the team puts on its own slice*. Placed after
the ladder it would leave a reader judging the team's promise without knowing the chain already holds
the tokens. Two figures here are **app-only**, keeping the split above intact: the **start date** and the
weekly **`0.94% of the supply`**. ⚠️ It is the **one block on that page allowed to say "locked"** —
everything around it is governed by the 2026-08-12 verb ban, which §5 explains is scoped to the ladder.

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

#### ⚠️ THE THIRD EXCEPTION — player balances and burn prices are COUNTS (#212, 2026-10-30)

The icon picker (burn price, `YOU HOLD` line), the profile popup `$STROKE` row and the first-time Space
Jackpot notice (`Hold at least N`, `You hold X`) state **token counts** of the player's own balance or a
configured price. Scoped to those three surfaces; supply shares everywhere else stay percentages. See
[`token-integration.md`](token-integration.md).

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

⚠️ **That programme's budget IS the `Community & game rewards` slice (30% since 2026-08-31, was 25%)**, not a second pot, and its
section says so in one line — the two sit two slots apart on the LP, one saying `250M` and one saying
`25%` about the same money. See [`feedbacker.md`](feedbacker.md), which also records an **open
arithmetic conflict between those season budgets and the treasury's Streamflow lock** that blocks the
next LP deploy.

> ⚠️ **READ §5 (the Streamflow lock) TOGETHER WITH THIS.** The lock and this exception were written
> the same day by different runs, minutes apart, and they interact: the lock puts the whole treasury —
> including the 25% slice the Feedbacker counts describe — on a **52-week linear release**, while the
> season budgets those counts state are **front-loaded**. [`feedbacker.md`](feedbacker.md) records the
> resulting arithmetic conflict, which blocks the next LP deploy.

### ⚠️ No em dashes in the guide's token copy

Owner, 2026-08-12: *"remove em dashes from the how-to-play#token (ai giveaway)."* Every `—` in the
**rendered** text of `GuideToken.vue` is now a comma, a colon or a full stop; the ones left in the file
are all inside code comments. It is a **punctuation** rule, not a rewrite licence — the sentences are the
owner's own and were only re-punctuated. ⚠️ `–` and ` - ` are **not** substitutes; same tell, different
glyph.

> ⚠️ **Scoped to `/how-to-play#token`.** The LP's own token section (including its `STRAIGHT TALK`
> pre-launch branch, which is marked final copy) still uses em dashes and was **not** swept. Widening
> this to the rest of either site is a separate decision.

⚠️ **The consequence: the supply model now exists in BOTH repos** (`monkify-lp/src/constants.ts` and
`monkify-front/src/constants/token.ts`) — `TREASURY_SLICES`, `TEAM_SLICE`, `SUPPLY_SLICES`,
`TREASURY_TOTAL`, `TEAM_PERCENT`, `HELD_TOTAL`, `PUBLIC_FLOAT_PERCENT` **and** `TEAM_WALLET`.
**A percentage or wallet change is a two-repo change.** The community/marketing division is still
`TODO(launch)` at both sources — only the 10% team slice came from the brief.

## 8. Analytics

One event, `token_click`, carrying a **`source` label and nothing else** — no wallet, no amount, no mint.
Values: `home_buy`, `home_read_more`, `menu`, `guide_buy`, `guide_supply`, plus (since #212)
`home_community`, `game_terminal`, `race_screen`, `icon_picker`, `jackpot_notice`, `menu_balance`,
`home_jupiter`. ⚠️ Since #212 `menu`, `guide_buy`, `home_community` and the other buy sources mean "went to
the in-app swap card", not "left for pump.fun"; `home_buy` is now the secondary pump.fun link and
`home_jupiter` the `Swap on Jupiter ↗` fallback.

⚠️ **The address bars are NOT instrumented, and the Streamflow `Verify ↗` link follows them.** Neither
the mint's `Solscan ↗` nor the treasury's fires anything, so the new vesting link does not either —
adding a sixth `source` value for it would be a decision about what the series means, not a tidy-up.

⚠️ **Deliberately NOT folded into `home_link_click`.** That series' documented meaning is *"which surface
do players reach for from the front door"*; this one counts *"how many people want to know about the
coin"*, wherever it is shown. `home_read_more` routes to `/how-to-play#token` (internal, since
2026-08-12) and `guide_supply` opens the DexScreener chart — so the three read as a **funnel**:
front-door card → guide → chart. See [`stats-and-history.md`](stats-and-history.md) → Analytics.

## 9. Open before the LIVE build

> ⚠️ **The live build already shipped** — `spacestroker.com` serves the live section with mint
> `Dts45D3WfhuJuwpK5nYkPM6Jyu5DqeEQzTw3rJkopump` and treasury `GqxFusRS3fHL8aLQoDyz4yPmwadc1zQ375WxJHhWqjme`.
> Items 1 and 2 below are therefore **settled by reality**, not still open; they are kept with their
> answers because the constants they name still carry stale `TODO(launch)` markers.

1. ~~**The ticker**~~ — **resolved: `$STROKE` is the real ticker.** The mint ends `…opump` and the
   Streamflow contract denominates in `STROKE`. ⚠️ **`TOKEN_TICKER` is still marked `TODO(launch)` in
   BOTH repos against a name that is now permanent on-chain** — the comment is stale, not the value.
2. ~~**The treasury percentages**~~ — **partly resolved.** The lock puts **49%** on-chain (§5), so the
   "sized to land the treasury on 50%" placeholder assumption is now checkable and ~1% out. The team's
   **10% is settled and now sits outside the treasury** (§5.1); the division of the treasury's 50%
   *between* community (30) and marketing (20) is still unconfirmed. ⚠️ The bar still renders `percent`
   as a raw flex weight and does **not** normalise, so slices that do not total 100 with the public
   float silently misrepresent the split.
5. ⚠️ **The team wallet has to be funded before the next LP/app deploy.** `2NBsPWJ5…rdGM` held **0 SOL
   and no token account for the mint** when the copy pointing at it was written (§5.1). Both sites link
   it to Solscan as proof of the 10%, so deploying ahead of the transfer publishes a verifiable claim
   that verifies as empty. **Still open.**
3. **Confirmation that buyback-and-burn and jackpot seeding are genuinely intended** — naming them in
   copy makes them a public promise. **Jackpot seeding: ANSWERED — it shipped 2026-09-16** at 100,000
   $STROKE per winner ([`../specs/jackpot-stroke-reward.md`](../specs/jackpot-stroke-reward.md)).
   ⚠️ It carries **~20 rounds of runway** (2,000,000 held ÷ 100,000, drawn daily), after which every
   round pays nobody and logs an Error — **topping the house wallet up is an ops task with a
   deadline**. **Buyback-and-burn: still open**, still future tense (§4).
   ⚠️ **The bonus and its runway are gone since 2026-10-01 (#196)** — Space Jackpot is now a free draw
   for holders and pays SOL only ([`space-jackpot-refactor.md`](space-jackpot-refactor.md)). There is no
   top-up deadline any more; what to do with the house's leftover $STROKE is an open owner question.
4. Whether the apex `spacestroker.com` or `www.` is canonical for any LP deep link. **Still open.**

## Key code touchpoints

**`monkify-lp`** (on `master`)

| File | Role |
| --- | --- |
| `src/constants.ts` | `TOKEN_MINT` / `TOKEN_LIVE` / `TREASURY_WALLET` (the base58 gate), `TOKEN_TICKER`, `TREASURY_SLICES` / `TEAM_SLICE` / `SUPPLY_SLICES` + derived totals (`TREASURY_TOTAL`, `TEAM_PERCENT`, `HELD_TOTAL`, `PUBLIC_FLOAT_PERCENT`), `TEAM_WALLET` / `TEAM_WALLET_URL`, the pump.fun / DexScreener / Solscan URLs, `shortMint`, `SOCIALS`, **and the lock block: `VESTING_CONTRACT` / `VESTING_URL` / `VEST_PERCENT` / `VEST_WEEKS` / `VEST_START_MS` / `VEST_END_LABEL` / `vestedWeeks()`** |
| `src/components/TokenSection.vue` | Both states — the live section and the "No token. Yet." panel. **The lock block, its `ready` gate and `.vest-meter` live here**; the pre-launch panel is untouched (no token ⇒ nothing to lock) |
| `src/pages/index.astro` | `#token` in slot 2, the rebalanced `ContentSection` props, the gated hero CTA |
| `src/components/Header.vue` | `Token` as the first nav item, gated |
| `src/components/HeroHud.vue` | The interactive hero corners (pump.fun status, socials, contract) — ⚠️ **not** `aria-hidden`, unlike the decorative HUD it was split out of |
| `src/components/SpaceRoadmap.vue` | Phase 5's de-staked copy |
| `src/components/PixelText.vue` · `PumpMark.vue` · `PixelIcon.vue` · `src/icons.ts` | Display-face glyph corrections and drawn marks |

**`monkify-front`**

| File | Role |
| --- | --- |
| `src/constants/token.ts` | `tokenLive`, `TOKEN_TICKER`, the URLs, the mirrored `TREASURY_SLICES` / `TEAM_SLICE` / `SUPPLY_SLICES` + totals, `TEAM_WALLET` / `TEAM_SOLSCAN_URL`, `UNLOCK_STEP_PERCENT`, **and the mirrored lock block** (same names as the LP plus `VEST_WEEKLY_PERCENT` and `VEST_START_LABEL`, ⚠️ the two app-only ones — see §7) |
| `src/components/home/HomeTokenCard.vue` | The front-door card |
| `src/components/guide/sections/GuideToken.vue` + `src/constants/guideSections.ts` | The conditional manual section (⚠️ `#token` still resolves safely on a build without it) |
| `src/components/GameHeader.vue` | The ☰ pad's token row |
| `src/hooks/useAnalytics.ts` | `trackTokenClick` |
| `src/components/home/HomeSection.vue` | The `header` slot that made `title` optional, for the one panel whose heading *is* the ticker |
| `src/components/ui/PixelText.vue` · `PumpMark.vue` · `MoneyValue.vue` | The ported `$` correction |

**`monkify-meta`** — `deploy/secrets.example.json` + `deploy/README.md` (`frontBuild`).

## Known gaps

- ~~**No browser QA was run** in either env state~~ — **closed 2026-08-17.** The **live-mint state has
  now been rendered in a browser on both surfaces**, at 1280px and at a true 360px (CDP viewport
  emulation, not `resize_page`): LP `#token` and app `/how-to-play#token`, zero console errors, no
  horizontal overflow, COPY verified on all three LP bars. The 2026-08-12 waiver stands for what shipped
  then; this covers the surfaces as they are now.
- `monkify-backend/scripts/setup_local_environment.bat` does **not** emit the two keys, so the local
  Docker stack can only exercise the pre-launch state. ⚠️ **Neither hub's env file carries them either**
  — rendering any token surface locally means adding the two public addresses by hand to
  `monkify-lp/.env` and `monkify-front/.env.local` (both gitignored) and reverting after, and
  `/how-to-play` **additionally needs the backend up** or the `> RECONNECTING…` overlay hides the page.
- ⚠️ **At 360px all three app address bars truncate the address to ~4 leading characters** (`Ar3…`,
  `Dts4…`, `GqxF…`), losing the tail a reader eyeball-matches against an explorer. Pre-existing
  `.addr-bar` behaviour, not introduced by the lock block — fixing it is one change across all three.
