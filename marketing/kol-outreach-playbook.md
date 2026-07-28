# X (Twitter) KOL Outreach Playbook — SpaceStroker / pump.fun token

Scope: **Solana / pump.fun degen accounts**, **1k–200k followers**, **English global CT +
Portuguese/BR**. Research date: **2026-07-28**.

Working files live in `kol-outreach-tracker/` — import each CSV as a tab:
`1-partners.csv` (the outreach tracker, pre-seeded with candidates and reply-mining anchors),
`2-sourcing.csv` (discovery surfaces + copy-paste X search queries),
`3-deal-tiers.csv` (per-tier allocation math + the vetting checklist).

Siblings: `twitter-launch-campaign.md` (organic X campaign), `telegram-launch-campaign.md`,
`promotion-channels.md` (Reddit/Telegram channel research), `utm-conventions.md` (the `utm_source`
vocabulary this playbook's tracked links use).

---

## 0. How the list was verified (read this first)

`x.com` returns **HTTP 402** to automated fetches, and every mirror (xcancel, nitter, sotwe,
lightbrd) plus socialblade and kick.com returns **403**. The one route that works is
**search-engine indexing of `x.com`**: searching a bare handle with results restricted to that
domain returns `Display Name (@handle) / X` plus indexed posts for a real account, and only
look-alikes for a dead one. Snippets frequently leak the follower count and display name too.

Every row in `kol-outreach-tracker/1-partners.csv` now carries a `Link status` column set by that
check. **What it caught, out of 23 handles taken from published lists:**

| Verdict | Count | Examples |
|---|---|---|
| Real and active | 15 | `@Sol_Ficra`, `@valueandtime`, `@rasmr_eth`, `@NotChaseColeman` |
| Real but stale / unconfirmed | 2 | `@Thorshammergems` (nothing newer than 2024), `@cryptolouca` |
| Real but a dead lead | 1 | `@CryptoStache` — publicly quit crypto content |
| **Did not exist at all** | **3** | `@GuruMemeCoin`, `@CryptoSolanaCeo`, `@bitcoindaily0` |

Two traps, because both produce plausible-looking dead links:
- **cryptokolz's "handles"** (`@cipher`, `@jury`, `@airdrop`) are marketplace *display names*, not X
  handles.
- **kolscan's `@Bi4rd5`-style strings are wallet-address prefixes**, not handles. Click through to
  get the real account.

**Two things still cannot be found from outside X**, and no amount of research fixes it:
- The **nano tier (1k–10k)** isn't search-indexed. Reply-guy mining (§2.4) is the only route, and it
  has to be done logged in.
- **PT-BR degen accounts** returned nothing in any external search — the Brazilian scene on X is
  genuinely unindexed. Lead the Brazil push with **Kick** (§2.7) instead.

---

## 1. Reality check on your offer

**What you're offering:** early/pre-launch access to a pump.fun token.
**What that is worth on CT today:** a lot to nano accounts, less than you'd think to anyone above ~30k.

Three things changed the market:

1. **Insider allocations are now publicly traceable.** kolscan.io, Bubblemaps and Arkham map KOL
   wallets and "shadow wallets" — an account that receives a pre-launch bag and shills it gets called
   out, and the chart gets branded a bundle. Accounts with a reputation to protect increasingly say
   no to unvested allocations, or demand the deal be public.
2. **A leaked spreadsheet of 200+ paid KOLs** (handles, price per post, SOL payout addresses, with
   on-chain receipts) went around and burned a lot of people. Undisclosed shilling is a liability for
   *you*, not just them.
3. **X's March 2026 policy change** — see §7. Crypto paid promo is *allowed* again; gambling is
   restricted by region; both now require a **"Paid Partnership" label**.

**Your actual edge — lead with this, not with the token:** you have a **live game with real
revenue**. Essentially no pump.fun project does. A KOL's #1 fear is shilling a rug and eating the
reputational cost. "The product is already live, here's the URL, go play it right now" removes that
fear, and it's the single strongest sentence in your DM.

**Recommended offer shape (hybrid) — see §8 for the mechanics and the economics correction:**

| | Nano (1k–10k) | Micro (10k–50k) | Mid (50k–200k) |
|---|---|---|---|
| Early-buy window (% supply cap) | 0.25% | 0.75% | 1.50% |
| Creator-fee revshare | 0.25% | 0.50% | 0.75% |
| Cash | No | Optional $100–500/post | Usually required $200–2,000/post |
| Posts required | 2 | 2 + 1 QT | 3 + pinned 24h |
| Expected yes-rate on cold DM | 15–30% | 5–15% | <5% |

Benchmarks from the market: small KOLs (5k–50k) charge **$100–$1,500 per post**; crypto-casino
affiliate norms are 25–50% revshare of *house* revenue — **which does not translate to this game;
see §8.1.**

**You cannot vest tokens someone buys themselves.** On a pump.fun bonding curve there is no
pre-mine to lock — "early access" means they get the CA before the public and buy on the curve.
Once bought, those tokens are theirs. Your only real enforcement lever is a **held-back bonus pool**
released weekly to partners who delivered *and* still hold, verifiable on-chain because you know
their wallet (§8.4).

**Cap total early-buy concentration.** If a visible slice of supply sits in known KOL wallets at
launch, Bubblemaps turns your launch into a "bundled" callout thread. Deploy roughly half your
budget in the launch window and stagger the rest.

---

## 2. Where to actually find these accounts (self-updating surfaces)

Ranked by signal quality for your use case.

### 2.1 kolscan.io/leaderboard — **best single surface**
Free Solana wallet tracker ranking KOLs by realised PnL, win rate, and trade history; it links wallets
to X handles and exposes shadow wallets. Verified working. Live top-of-board names when I checked:
theo, Kev, OGAntD, Doji, Cented, Fox, cap, Sheep, Loopierr.

Use it two ways:
- **Discovery:** scroll past the top 20 (too big / not for hire) into positions 20–50 — that's where
  small accounts with *actual proven calls* sit.
- **Vetting:** before paying anyone, pull their wallet. If they dump every call within minutes,
  their audience knows, and your token gets the same treatment.

### 2.2 pump.fun's own surfaces
- **`pump.fun/live`** — livestreamers running coins. Small streamers are the most DM-receptive
  group on the entire platform and their audience is 100% degens who already have a funded Solana
  wallet. Highest player-conversion channel for you.
- **Callouts + global caller leaderboard** (pump.fun mobile app) — callers rank publicly, one call
  per 6h, push-notified to their followers. A ranked-but-small caller is exactly your target profile.
- **CTO communities** — pump.fun now routes creator fees to community takeovers. CTO raiders are
  organised, unpaid, and permanently looking for a project that will actually pay them. Very
  underpriced.

### 2.3 X Communities
`https://x.com/i/communities/1804846498066116981` — "Pump.Fun: Shill your coin", ~8.2K members.
A room whose entire purpose is shilling coins. Read it for a week, DM the members whose posts
actually get replies (not the ones spamming into the void).

### 2.4 Reply-guy mining — **highest ROI per hour, do this one first**
Open a big anchor account's recent posts and harvest the **repliers with 1k–20k followers whose
replies get real engagement**. They have degen audiences, they're active daily, and nobody is
bidding for them.

Anchors to mine (all real, all too big to DM cold — use them as fishing spots):
`@blknoiz06` (Ansem, ~750k), `@theunipcs`, `@Orangie`, `@NotChaseColeman`, `@FrankDeGods`,
`@a1lon9` (pump.fun co-founder), `@Pumpfun`.

Same trick on **quote-tweets of recent pump.fun graduations**: who called it early with <20k
followers? Those people are proven and cheap.

### 2.5 Kaito yapper leaderboards
Ranks accounts by AI-scored "mindshare" per project/ecosystem. Filter to Solana, then look at
ranks 50–300 — real influence, small following, and they already understand allocation-for-content
deals because that's how Kaito campaigns pay.

### 2.6 If you'd rather pay cash than hunt
- **Lever.io** — self-serve marketplace, 500+ vetted Web3 KOLs, escrow holds funds until delivery.
- **Coinzilla KOL Spotlight** — fixed packages, €1,999 / €4,999 / €9,999.
- Managed agencies (overkill at your stage, $5k+ minimums): Coinbound, NinjaPromo, Lunar Strategy,
  TokenMinds, Surgence, Coinband.
- **theKOLLAB** is the one structurally aligned with your offer — it runs **token-allocation KOL
  rounds** where creators take a stake instead of cash. Worth a conversation.

### 2.7 Brazil / PT-BR — honest finding
**BR degen memecoin CT does not appear in any published list.** Every "top Brazilian crypto
influencer" listicle returns education/YouTube personalities — Fernando Ulrich, Caio Vicentino,
BitNada (Felipe Escudero), Criptomaniacos (Guilherme Rennó), Investimentos Digitais, Thata Saeter,
EddieOz, Richard Rytenband, Rodrix, UseCripto. Those are **the wrong audience** (long-form investor
education) and most will not touch a betting product.

Where the BR degen/gambling audience actually is:
1. **Portuguese-language X search** (queries in §3) — the BR degen scene is real but unindexed.
2. **Kick crypto-casino streamers (PT-BR)** — Brazil is one of the largest casino-stream audiences in
   the world. These creators already run affiliate/revshare deals, already have a deposit-ready
   audience, and your offer speaks their exact language. **This is your best BR channel, and it's not
   on X.** Browse `kick.com/category/slots` and filter by language. Named candidates already in the
   tracker: **`kick.com/prizao`**, **`kick.com/BAIANOTV`** (both Brazilian), plus
   `kick.com/brunenger`, `kick.com/spinlife`, `kick.com/axt` (language unconfirmed). Kick blocks
   automated reads, so follower counts are a manual pass.
3. BR crypto Telegram/Discord call groups — find them by searching X for `t.me` links in PT posts.

**And re-read our own `promotion-channels.md`.** Its §5.7 already verified **12 casino streamers**
with follower counts and check dates — including **`kick.com/schneckyirl` (45.8k, caught LIVE in
Slots & Casino)**, the smallest and most reachable of them. That doc assessed them for a **$0 organic**
approach and concluded it was a long shot. **Our offer isn't $0 organic** — allocation plus creator
fees changes their answer, so those rows are now partner leads, not just reply targets. They're
carried into the tracker.

---

### 2.8 Competitors the search turned up — do not DM, do read

Four Solana wager products surfaced while hunting partners. They're in the tracker flagged
`COMPETITOR` so nobody pitches them by accident:

- **`@solanacrashio`** — a Solana crash game with **provably-fair** positioning. The closest thing
  found to us: same chain, same trust angle, adjacent mechanic. Read their feed before you write
  partner terms — they are competing for the same KOLs.
- **`@PvPSolanaCasino`** — player-versus-player casino. Structurally the nearest match to our
  pot-based, player-funded model.
- **`@CasinoSolana`**, **`@degencom`** — a club-style project and a platform-scale casino. Degen.com's
  published affiliate terms are a useful benchmark for what partners will compare your offer against.

One more account worth reading and not contacting: **`@0xmani`**, who publicly calls out "washed KOLs
teaming up to extract the last bit of liquidity from memecoins". That's the exact reaction a
20%-to-partners launch invites. Read it before you finalise the allocation caps (§8.4).

---

## 3. Exact X search queries — copy/paste these

Run in X search, then switch to the **Latest** tab, and check each poster's follower count manually.
Note: X search matches *post text*, not bios — for bio matching use the **People** tab.

**English — accounts openly selling promo:**
```
("dm for promo" OR "promo rates" OR "paid promo" OR "collabs open" OR "dm for collab") (solana OR pumpfun OR memecoin) -filter:replies
```

That one is proven: **`@Sol_Ficra` and `@Cryptosage500` both carry "DM for promo" in their bio** and
were found exactly this way.

**English — first-time paid-promo takers (cheapest quality on the market):**
```
("first paid promo" OR "first sponsored" OR "first collab") (solana OR memecoin) -filter:replies
```
Found `@genuinearticles`, who posted "The first paid promo deal I've ever taken. Solana." No rate
card yet, audience not yet burnt.

**English — gambling × memecoin crossover (they get both halves without explanation):**
```
(solana) (casino OR wager OR gambling OR degen) (trenches OR memecoin OR pumpfun) -filter:replies
```
Found `@Degen_Hardy` and `@Overdose_AI`.

**English — active small callers:**
```
(pumpfun OR "pump.fun" OR $sol) (calling OR "ape" OR "sending it" OR "up 10x") min_faves:15 -filter:replies lang:en
```
Tune `min_faves` (10 → 50) to slide the size band. `min_faves:15` with a small follower count is a
*high engagement rate* — exactly what you want.

**English — GameFi/degen-gaming crossover:**
```
(solana) (game OR gaming OR "play to earn" OR wager) min_faves:10 -filter:replies lang:en
```

**Portuguese — BR degens:**
```
(memecoin OR "moeda meme" OR pumpfun OR solana) lang:pt min_faves:5 -filter:replies
```
```
("parceria" OR "publi" OR "divulgação" OR "fecho parceria") (cripto OR solana OR memecoin) lang:pt
```
```
(cassino OR aposta OR "cripto cassino") lang:pt min_faves:10 -filter:replies
```

**Find who's already promoting competitors** (best-qualified leads on the list):
```
(rollbit OR stake.com OR "crypto casino" OR degenwars) (solana OR sol) -filter:replies
```

**Volume target:** to land 10–15 partners you should expect to build a list of **150–250 accounts**
and send **~100–150 DMs**. Plan for that, not for 20.

---

## 4. Vetting checklist — run before every DM

Kill the lead if it fails 2+:

- [ ] **Engagement ratio** — likes ≥ ~1% of followers on normal posts. Below 0.3% = dead or bought.
- [ ] **Reply quality** — are the same 20–30 accounts liking every post? That's an engagement pod.
      Their "audience" is other shills, not buyers.
- [ ] **Last 20 calls** — paste their recent tickers into DexScreener. All instant -90%? Their
      followers stopped buying their calls long ago.
- [ ] **kolscan wallet** — do they hold their calls or dump in 4 minutes?
- [ ] **Account age & renames** — new account + generic name = likely purchased/recycled.
- [ ] **Audience geography** — you need people who can actually deposit and play. An account whose
      replies are all in one language you don't serve is worthless to you regardless of size.
- [ ] **Do they disclose?** An account that never labels sponsored posts is a compliance liability
      (§7) and a sign they've burned their audience already.

Remember: *a 50k account at 8% engagement beats a 500k account at 0.3%* — and costs 10x less.

---

## 5. DM templates

Rules: short, no corporate tone, no attachments, lead with the live product, one ask. Follow up
**once** after 3–4 days, then drop it.

### 5.1 Nano / micro, EN (token-led)

> yo — building SpaceStroker, a solana wager game. it's **live already**, not a whitepaper:
> app.spacestroker.com — go break it, takes 30 seconds
>
> we're launching our token on pump.fun soon and setting aside allocation for a small group of
> accounts who post about it before + at launch. vested so nobody nukes the chart.
>
> on top of that: **[X]% of the pump.fun creator fees**, paid weekly for as long as you're posting.
> that's fee revenue, not token price — you get paid even on red days.
>
> interested? happy to send the one-pager w/ exact numbers.

### 5.2 Micro / mid, EN (revshare-led — for accounts that would ignore a token offer)

> hey — SpaceStroker, solana wager game, live and taking real bets today:
> app.spacestroker.com
>
> not asking for a free shill. offering:
> • **[X]% of pump.fun creator fees**, paid weekly in SOL, for as long as you're posting
> • the CA [X] hours before public, capped buy so it's not a bundle
> • a held-back token bonus that keeps paying while you hold
> • cash for the first post if you'd rather de-risk it — what's your rate?
>
> you can verify the product works before you post anything. that's the whole point.
>
> worth a 5 min call or should I send numbers here?

### 5.3 PT-BR

> fala! tô construindo o SpaceStroker, um jogo de aposta na solana. **já tá no ar**, não é promessa:
> app.spacestroker.com — testa aí, leva 30s
>
> vamos lançar o token na pump.fun em breve e separamos alocação pra um grupo pequeno de perfis que
> postarem antes e no lançamento. com vesting, pra ninguém derrubar o gráfico.
>
> além disso: **[X]% das creator fees da pump.fun**, pago toda semana em SOL enquanto você postar.
> é receita de taxa, não preço do token — você recebe até em dia vermelho.
>
> topa? mando o one-pager com os números exatos.

### 5.4 Follow-up (3–4 days later, once)

> bumping this once in case it got buried — product's live if you want to look:
> app.spacestroker.com. no worries either way 👍

### 5.5 One-pager you should have ready before sending any DM
Token launch date/venue · allocation size + vesting schedule · revshare % and payout cadence ·
tracked-link mechanics · what exactly you expect (how many posts, when, format) · disclosure
requirement · your contact / who they're dealing with.

---

## 6. Tracker (build this in a sheet before DM #1)

| Handle | Followers | Est. engagement % | Niche | Lang | Source | Vetted? | DM sent | Reply | Deal | Alloc | Revshare % | Posts due | Players sent |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|

Track **players sent**, not impressions. Impressions are what agencies sell; players are what you
asked for.

---

## 7. Policy & compliance notes (short, but these bite)

- **X, March 2026:** cryptocurrency was **removed** from the prohibited-industries list for paid
  promotions (reversing the June 2024 ban). Crypto promo is allowed again.
- **Gambling is the nuance.** Financial/gambling promos remain **restricted in the UK, EU and
  Australia**; EU campaigns need individual approval as an "adult category". Your game is a wager
  product — **weight your list toward global CT and LATAM, not UK/EU accounts.**
- **"Paid Partnership" label is mandatory** for anything given in exchange for money, in-kind
  benefits, affiliate commission, or ambassador arrangements — **a token allocation counts as an
  in-kind benefit.** Non-disclosure triggers automated warnings; repeat violations risk suspension.
  Put "you must use the Paid Partnership label" in your one-pager. It protects the partner *and* it
  makes you look like the one project that isn't running a scam.
- Influencers are responsible for their own legal compliance (FTC endorsement rules etc.), but a
  project that instructs partners to hide the deal is the project that ends up in the next leak.

---

## 8. Attribution & reward mechanics

Settled parameters: **campaign runs before the token launch**; **20% of supply** earmarked across all
partners; **no geo restrictions** on who can play; **the reward is early-buy access for now**.

### 8.1 Economics correction — you have no house edge to share

Space Stroker is **peer-to-peer**: players bet into a pot and winners take the *other players'*
stakes. The house's only take on crypto games is the **0.5% commission** on winnings above each
winner's own stake (`GeneralSettings.Token.CommissionPercentage` — see
`monkify-meta/features/crypto-commission.md`), which works out to ≈0.5% of losing stakes. There is no
player-loss margin to give away.

So a "30% revshare" like a casino affiliate deal = 30% × 0.5% ≈ **0.15% of wagered volume**. On
$100k of turnover that's $150, split across every partner. Nobody moves for that, and anything you
promise above ~0.25% of turnover is loss-making.

**Conclusion: do not pitch game revshare. Pitch pump.fun creator fees.** They're real, recurring,
cost you nothing upfront, and scale with exactly the behaviour you're buying (trading volume).
It also needs no explaining — pump.fun already routes creator fees to community contributors through
the CTO feature, so the mechanic is native to their world.

### 8.2 Attribution — three options, ranked

**Option A — UTM links only. Zero code, ship today. ✅ do this now.**
GA4 is already live on both `www.` and `app.spacestroker.com` as one property with a shared session
across the hop (`features/analytics.md`), and `marketing/utm-conventions.md` already fixes the
`utm_source` vocabulary. Give each partner
`https://app.spacestroker.com/?utm_source=kol&utm_campaign=<handle>`.
- **Gets you:** clicks, wallet-connect attempts/successes, `bet_placed` counts, `game_type_selected`
  — per partner. Enough to rank partners and decide who to renew.
- **Does not get you:** money. GA4 deliberately carries **no stake or payout amounts and no wallet
  identity**, and it's blocked for 20–40% of a crypto-savvy audience. **Never pay a % of revenue off
  GA4 numbers.**

**Option B — a referral code on the player record. Small backend feature; the right answer once you
pay on volume.**
The identity model already exists: `OperatorPlayer` (operator-scoped player), and every `Bet` carries
`OperatorPlayerId`, `PaymentMethod` and `ExternalTransactionReference`. Minimal shape: capture
`?ref=<code>` in the SPA → send it with the first bet → persist a nullable
`OperatorPlayer.ReferredBy` (+ index) → aggregate wagered volume per code behind an admin read
endpoint. One column, one migration, one query. Revshare then computes off the **ledger**, not off
analytics.
- Gotcha: `SpaceStrokerDbContext` is **NoTracking by default** — load-mutate-`SaveChanges` silently
  persists nothing. Use `ExecuteUpdate` or an explicit `Update()`.
- Per the KB rules this counts as a new feature ⇒ mandatory happy-path integration test.
- **Don't block the DM campaign on it.** Ship A now, build B when money depends on it.

**Option C — one Operator per partner. Rejected.**
Superficially free (bets are already operator-scoped, so per-partner revenue is queryable with no
schema change) — but operator scoping **segregates games**. Each partner's players would sit in their
own rounds and never reach `MinimumNumberOfPlayers`. It fragments precisely the liquidity you're
trying to build. Do not do this.

### 8.3 What to reward with — menu, cheapest-first

1. **Early CA access** — their stated ask. The entry ticket, not the whole deal.
2. **Creator-fee share**, paid weekly in SOL to a wallet they name. The recurring hook, and the
   thing that makes you look like a business rather than a launch.
3. **Held-back token bonus contingent on still holding** — released weekly, verifiable on-chain
   because you know their wallet. Your *only* real enforcement lever (see 8.4).
4. **Milestone bounties instead of per-post fees** — pay on N referred wallets placing a first bet,
   or on filling a round. Aligns them with players, not impressions.
5. **A named game type — nearly free, priced like status.** The admin portal already creates game
   types (`features/operator-admin-page.md`), so "*@handle's Sprint*" is a config row, not a build.
   Extremely cheap and unusually sticky; nobody else can offer it.
6. **First-play credit for their audience — NOT currently shippable.** Free-bet mode exists, but for
   the Main operator it's `AllowFreeBet = !IsProduction()` and off in production
   (`features/main-only-player-entry.md`). Don't promise it in a DM until someone builds a real promo
   credit.
7. **A partner leaderboard / competition** — cheap, and it re-activates the ones who'd otherwise
   ghost after post #1.

### 8.4 On the 20% — one flag, then it's your call

Two consequences worth naming before you commit it:

- **Concentration reads as a bundle.** Identifiable early wallets holding ~15%+ of supply is the
  exact pattern the audience you're targeting screenshots and calls out — kolscan and Bubblemaps make
  it a 30-second check. The people best at spotting it are the same people you're DMing.
- **You cannot claw any of it back.** They buy on the curve, so the tokens are theirs the moment they
  land. 20% bought cheap is 20% of supply sitting above your first pump as exit liquidity.

**Recommendation:** keep 20% as the *budget*, but deploy only **10–12% in the launch window** (the
vetted half of the roster, with a per-partner cap — see the deal-tiers tab) and hold **8–10% as a
performance-contingent bonus pool** released weekly to partners who delivered and still hold. Same
total spend, a chart that survives, and you stop paying the ones who ghost.

Also: **set a per-partner cap, not just a global one.** 20% across 15 partners is 1.33% each; across
40 it's 0.5%. Decide the cap first, then the roster size follows.

### 8.5 No geo restrictions — the consequence

Good: your target list widens (LATAM, SEA, global CT all in play). But X's paid-partnership rules
still restrict **gambling** promo in the UK/EU/Australia (§7) — that constrains **your partners'
posts**, not your players. Weight the roster away from UK/EU-based accounts.

---

## Sources

- [kolscan leaderboard](https://kolscan.io/leaderboard) · [kolscan overview — Bitget](https://web3.bitget.com/crypto-news/unmasking-the-alpha-how-kolscan-io-is-changing-the-memecoin-discovery-game)
- [Pump.fun CTO creator fees announcement](https://x.com/a1lon9/status/1946290531929100598) · [Pump.fun callouts + caller leaderboard](https://x.com/Pumpfun/status/2011865917160907160) · [Pump.fun live](https://pump.fun/live)
- [X Community: "Pump.Fun: Shill your coin"](https://x.com/i/communities/1804846498066116981)
- [25 Top Meme Coin Influencers on Twitter — BonkBot](https://bonkbot.io/library/crypto-memecoin-twitter-influencers)
- [Top 90 Crypto Influencers — Feedspot](https://x.feedspot.com/crypto_twitter_influencers/)
- [Best Crypto KOL Marketing Platforms 2026 — FORKOFF](https://forkoff.xyz/blog/influencer-marketing/best-crypto-kol-marketing-platforms-2026)
- [Crypto influencer pricing — CryptoKolz](https://cryptokolz.com/crypto-influencer-pricing)
- [Crypto casino marketing — KOL HQ](https://www.kolhq.com/blog/crypto-casino-marketing-win-players-2025) · [iGaming Marketing Lab](https://www.igamingmarketinglab.com/blog/crypto-casino-marketing-strategy)
- [X lifts crypto/gambling paid-promo ban — Dataconomy](https://dataconomy.com/2026/03/02/x-lifts-cryptocurrency-and-gambling-bans-for-paid-promotions/) · [BeInCrypto](https://beincrypto.com/x-lifts-crypto-gambling-ban-paid-promotions/) · [iGaming Expert](https://igamingexpert.com/regions/europe/x-twitter-gambling-paid-partner/) · [Social Media Today](https://www.socialmediatoday.com/news/x-formerly-twitter-adds-paid-partnership-tags/813573/)
- [Undisclosed paid KOL exposé — Gate Learn](https://www.gate.com/learn/articles/overseas-kols-playbook-for-fleecing-retail-coordination-hype-and-concealment/11893)
- [Top 10 cripto influencers brasileiros — CoinGape BR](https://br.coingape.com/top-10-cripto-influencers-brasileiros-acompanhar/)
- [Ansem / @blknoiz06 profile — Phemex](https://phemex.com/academy/who-is-ansem-solana-trader-ansem-token-frenzy)
