# Space Stroker — Referral partner targets (cold outreach)

> **Purpose.** Named, live-verified targets for the **referral programme**
> ([`../features/referral-program.md`](../features/referral-program.md) +
> [`../features/referral-cash-out.md`](../features/referral-cash-out.md)) — people we would cold-DM
> and hand a `?ref=` code to. This is an **outreach** list, not a promotion list.
>
> **Sibling doc:** [`promotion-channels.md`](promotion-channels.md) (#100, 2026-07-26) is where we go
> to *post*. This doc is who we *pay*. They deliberately do not overlap: that doc's streamer section
> leads with 600k–1M creators, which §2 below explains is the wrong end of the market for us.
>
> ⚠️ **`promotion-channels.md` is stale on product facts.** It states "token-free: USDC on Solana".
> Settlement moved to **native SOL**, the house cut is now **5%**, and a pump.fun launch token exists
> as UI. Do not paste its pitch lines into a DM without re-reading §1 here.
>
> **Verified:** every row opened live in a signed-in browser on **2026-08-27**. Nothing from model
> memory. Where a number could not be parsed it says so rather than guessing.

---

## 0. Sixty-second version

1. **Do not cold-DM the big gambling streamers.** §2 shows, with evidence from their own bios, that
   the ones worth having are contractually tied to Stake / Shuffle / Gamdom and publish that casino's
   code in their bio. We cannot outbid them and they cannot take us.
2. **The realistic pool is small crypto-native accounts (1k–30k) whose audience already holds SOL** —
   §3, Tier 1. Their followers can play in one click; a slots-stream audience mostly cannot.
3. **Offer 1.0% of turnover.** §1 derives the ceiling: above ~2.5% we pay a partner more than their
   traffic earned us. ⚠️ The rate is **permanent per code** — you cannot raise it later without
   re-pricing their whole history.
4. **Lead the DM with the payout page, not the game.** Self-serve SOL cash-out at `/partner` is the
   part no competitor affiliate programme has (§4).
5. Onboarding a partner is a **hand-run SQL insert** — production ships with zero real partners (§5).

---

## 1. What we are actually offering (read before writing any DM)

Product facts, all current as of this doc:

| | |
| --- | --- |
| Product | Space Stroker — multiplayer betting game on Solana. LP `spacestroker.com`, app `app.spacestroker.com` |
| Settles in | **Native SOL** (not USDC — that changed) |
| Fairness | Provably fair, publicly verifiable |
| House cut | **5%** of net winnings |
| Partner link | `https://spacestroker.com?ref=<code>` — works on apex, `www.` and the app |
| Attribution | **First-touch, wallet-bound, permanent.** 30-day capture window in the browser |
| Basis | **Turnover** — `SharePercentage × Σ paid stakes` by referred players, settled rounds only |
| Includes | **Orbit auto-bets** — a referred player's bot bets keep earning the partner money while they sleep |
| Payout | Partner connects their wallet at `/partner`, reads earned / paid / available, presses **Cash out**, real SOL lands from a dedicated payout account |

### The rate ceiling — the one number to not get wrong

Commission is `rate × (winnerReward − ownStake)`. With `N` players and one winner the house keeps
`rate × (N−1)/N` of turnover. So:

| Round shape | House keeps, as % of turnover | Max share before we lose money |
| --- | --- | --- |
| Heads-up (2 players) | 2.5% | **2.5%** |
| 5 players | 4.0% | 4.0% |
| Large lobby | → 5.0% | → 5.0% |

**Use 2.5% as the hard ceiling** — it is the worst case, and heads-up rounds are common. **Offer 1.0%**
and keep the headroom. 1% of turnover is a genuinely competitive affiliate number because it is paid on
*volume*, not on net losses.

⚠️ **The rate is effectively permanent.** `SharePercentage` is applied to all-time turnover on every
cash-out, so editing it **re-prices history already paid on**. The ops rule is *do not re-rate a live
partner — issue a new code*. Never promise a partner "we'll bump you to 2% if it goes well."

### Two things you must disclose, because they will find out

- **We cannot claim their existing players.** A wallet that has already bet has a non-NULL referral and
  can never be attributed. A partner whose audience already plays gets paid for **new wallets only**.
- **Attribution started 2026-08-21** and there is deliberately no backfill.

---

## 2. Why the big gambling streamers are a dead end — with receipts

Every large Slots & Casino streamer checked publishes a **rival casino's affiliate code in their own
bio**. This is not a guess; it is what their profiles said on 2026-08-27:

| Creator | Their own bio / stream title | Tied to |
| --- | --- | --- |
| `@BigFoltz` | "Entertainment & Recreation — **Stake Code: Bigfoltz \| #AD**" | Stake |
| `@KyleTooReal` | "Partner @shufflecom · **Shuffle Code: Highlights \| #AD**" | Shuffle |
| Prodigyddk (Kick) | stream title "BONER WEEK 2 **!STAKE**" | Stake |
| LadyLuckSlots (Kick) | stream title "VIEWER TOURNEY FOR CASH ! 18+**STAKE**" | Stake |
| Spinlife (Kick) | stream title "**!Gamdom** In Chat For Best Casino Offer!" | Gamdom |

These are exclusive-ish commercial relationships with a leaderboard and a monthly guarantee attached
(`@BigFoltz`: "$1M Monthly"; `@KyleTooReal`: "$15,000 Monthly Leaderboard"). A 1%-of-turnover referral
code from an unknown Solana game does not compete with that, and several of them have DMs closed anyway.

**There is a second, deeper mismatch.** A slots audience deposits with a card into a custodial casino.
Space Stroker requires a **self-custody Solana wallet with SOL in it**. That is a hard funnel for a slots
viewer and an easy one for a crypto-native follower. Bet on audience overlap, not on follower count.

**Where they are still useful:** as a *paid* one-off (a sponsored segment quoted in cash), not as a
referral partner. That is a different budget and a different conversation — park it.

---

## 3. The targets

Legend — **DM**: ✅ = X "Message" button present on 2026-08-27 (unsolicited DMs open) · ❌ = closed.
Sizes are followers unless stated.

### 3.1 Tier 1 — crypto-native, small, DMs open · **start here**

These are the ones to write to this week.

| id | Handle | Size | Activity (2026-08-27) | DM | Why them |
| --- | --- | --- | --- | --- | --- |
| `RP-01` | [@swarmy007](https://x.com/swarmy007) | 3,875 | 2,151 posts, joined Mar 2024, **followed by @Solana** | ✅ | **The single best fit found.** Bio: "Daily gambling & crypto chaos \| Bets, leaks & degeneracy" — gambling *and* crypto, already the exact overlap. Small enough that a founder DM is an event, and @Solana following them is real credibility. |
| `RP-02` | [@KorteDH](https://x.com/KorteDH) | 1,487 | 8,942 posts, joined 2015 | ✅ | "Korte — Solana Bando", runs `Bulltoshi.trade`. **Already ran a branded challenge with a gambling product (Dicey)** — proven willing to collaborate, and knows how to run a promo. Tiny following but an operator, not a lurker. |
| `RP-03` | [@Onchaingamblr](https://x.com/Onchaingamblr) | 1,775 | 78.9K posts, joined Aug 2023 | ✅ | Bio: "Degen. Gambler. I gamble on memes and myself." Extremely high posting volume for the follower count — an engaged niche account, cheap to win over. |
| `RP-04` | [@RadTrades_](https://x.com/RadTrades_) | 27K | 22.7K posts, **followed by Ansem** | ✅ | Was posting unpaid-looking praise for MetaWin ("the best crypto casino and it's not even CLOSE") — **proven to promote crypto casinos to a crypto audience**. Biggest Tier-1 reach. Expect them to ask for cash on top of rev-share. |
| `RP-05` | [@miladydegens](https://x.com/miladydegens) | 18.7K | 1,090 posts, joined Nov 2024, Telegram in bio | ✅ | "degen calls" — a calls channel with a Telegram. Pure degen audience that already signs Solana transactions. Lower quality of attention, but volume and a second surface (TG). |

### 3.2 Tier 2 — worth a message, with a caveat

| id | Handle | Size | Activity (2026-08-27) | DM | Why / caveat |
| --- | --- | --- | --- | --- | --- |
| `RP-06` | [@FabianoSolana](https://x.com/FabianoSolana) | 81.9K | 59.3K posts, joined Oct 2021 | ✅ | "Making a living with Defi" — **the "serious small-cap, not memes" account you asked for.** Writes real project analysis. ⚠️ A DeFi analyst may not want a gambling code on their timeline; pitch the *provably-fair / on-chain settlement* angle, not the "degen" one. |
| `RP-07` | [@NotSoEasyMoney](https://x.com/NotSoEasyMoney) | 148.8K | 85.5K posts, streams Mon–Fri 9am EST | ✅ | **Publishes a self-serve sponsorship product** — `SponsorEasy.app`, "steal a spot, get airtime". So there is a *documented, priced* way in that is not a cold ask. ⚠️ That path is **paid**, not rev-share. Treat as the paid-option benchmark. |
| `RP-08` | [@streampalX](https://x.com/streampalX) | 536 | 790 posts, joined Feb 2024 | ✅ | A streamer **network**, not a person: "THE HUB / NETWORK FOR ANYTHING STREAMING RELATED… DM US OR SIGN UP". One DM could reach many small streamers at once. ⚠️ Runs the **Roobet** clipping program — conflicted, and tiny. Cheap lottery ticket. |
| `RP-09` | [@cryptogamblingx](https://x.com/cryptogamblingx) | 3,675 | 13.1K posts, joined Feb 2018 | ❌ | "Crypto Gambling News — industry news & casino reviews." Perfect audience, **DMs closed**. Reach via a public reply or the linktree in their bio. A *review* from them is worth more than a code. |
| `RP-10` | [@KyleTooReal](https://x.com/KyleTooReal) | 582 posts; Kick **40.8k** | live in Slots & Casino at check | ✅ | The **only** large Kick streamer found with open X DMs. ⚠️ Shuffle partner (§2) so a code swap is near-impossible — but reachable, and worth one message to learn what a real streamer deal costs. |

### 3.3 Kick — Slots & Casino, verified live on 2026-08-27

All were **live in Slots & Casino at the moment of the check** — this is a measured pulse, not a
directory listing. Follower counts read off the channel page. Ranked small-first, since small is the
point.

| id | Channel | Followers | Viewers at check | Tied to | Socials found |
| --- | --- | --- | --- | --- | --- |
| `RP-KK-01` | [Spinlife](https://kick.com/spinlife) | 15,000 | 2,200 | **Gamdom** (in title) | not captured |
| `RP-KK-02` | [BigBetBaba](https://kick.com/bigbetbaba) | 16,400 | 1,100 | none seen in title | not captured |
| `RP-KK-03` | [LadyLuckSlots](https://kick.com/ladyluckslots) | 18,900 | not captured | **Stake** (in title) | not captured |
| `RP-KK-04` | [rydurz](https://kick.com/rydurz) | 27,800 | 1,800 | none seen in title | not captured |
| `RP-KK-05` | [Bigfoltz](https://kick.com/bigfoltz) | 34,200 | 1,500 | **Stake** (bio code) | [x.com/Bigfoltz](https://x.com/Bigfoltz) ❌ DMs closed · IG · YT · Discord |
| `RP-KK-06` | [SweetFlips](https://kick.com/sweetflips) | 35,600 | not captured | none seen in title | not captured |
| `RP-KK-07` | [KyleTooReal](https://kick.com/kyletooreal) | 40,800 | not captured | **Shuffle** (bio code) | [x.com/KyleTooReal](https://x.com/KyleTooReal) ✅ · IG · Discord |
| `RP-KK-08` | [Prodigyddk](https://kick.com/prodigyddk) | 51,400 | 1,800 | **Stake** (in title) | YouTube only — **no X found** |
| `RP-KK-09` | [schneckyirl](https://kick.com/schneckyirl) | not re-read | 2,200 | own site leaderboard | carried from `promotion-channels.md` `KK-06` |

> **The honest read on this table:** every one of them is a *slots* streamer. Space Stroker is not a
> slot. The realistic ask is **one novelty segment**, not a code swap — and `RP-KK-01`/`RP-KK-02`
> (smallest, no visible exclusive in the title) are the two to try first.

### 3.4 Assessed and rejected — do not re-research these

| Handle | Size | Why rejected |
| --- | --- | --- |
| [@solanagambling](https://x.com/solanagambling) | 165 | Bio literally "DM to collab", 24 posts since 2022. Bait/dead. |
| [@Gamblorss](https://x.com/Gamblorss) | 47 | **1 post** since Nov 2022. Dead. |
| [@degenhoods](https://x.com/degenhoods) | 388 | 7 posts, joined Dec 2025. Nothing there. |
| [@cryptogambORG](https://x.com/cryptogambORG) | 659 | 25 posts, dormant since 2017. DMs closed. |
| [@bored2boars](https://x.com/bored2boars) | 418 | Self-described secondary account. DMs closed. |
| [@OGDfarmer](https://x.com/OGDfarmer) | 127.7K | Good account, **DMs closed**. Reply-only. |
| [@Kryp_Toon](https://x.com/Kryp_Toon) | 1.3M | **Paid marketing agency** ("Advertising & Marketing Agency", "For Marketing PM"). Was running a *Paid partnership* post for a rival Solana casino. Buys reach, does not lend credibility. |
| [@Tork_Lab](https://x.com/Tork_Lab) | 205.1K | Same shape — paid AMA/giveaway agency. Same verdict. |
| [@luckyfun](https://x.com/luckyfun) | 14.7K | **Is a crypto casino.** Competitor. |
| [@0xSweep](https://x.com/0xSweep) | large | Co-founder of GlydeGG — runs a competing platform. |

### 3.5 ⚠️ Competitors found while searching — worth watching

Not partners; recorded because they turned up and matter.

- **[@SoltoGames](https://x.com/SoltoGames)** (`solto.io`) — Solana casino shipping "one new game every
  week", provably fair, and **already buying paid partnership posts**. Closest visible competitor for
  attention. Their promo posts are a good place to find promoters (that is how several `RP-` candidates
  surfaced).
- **[@coinwagerbet](https://x.com/coinwagerbet)** (`coinwager.bet`, 87 followers, joined Feb 2026) —
  ⚠️ **an almost exact positioning clone**: "The leading wallet-first platform on Solana. No KYC. No
  passwords. Just connect. Every bet is provably fair. PvP games you won't find anywhere else." Tiny
  today. Worth watching, and a reason not to sit on the wallet-first / provably-fair messaging.

---

## 4. The DM — what to actually send

**The lead is the payout page.** Almost every affiliate programme in this space pays by hand, monthly,
after a spreadsheet. We have a self-serve dashboard and an on-chain cash-out. That is the hook.

Keep it under ~120 words, no pitch deck, no calendar link in the first message.

```
hey — I build Space Stroker, a multiplayer betting game on Solana. Provably fair,
settles in native SOL, no KYC.

Not asking for a shoutout. We just shipped a partner programme and I'd rather give
a code to someone whose audience already has a wallet than pay an agency.

You'd get: your own ?ref= link, 1% of everything your players wager (their auto-bets
included), and a /partner page where you connect your wallet, see what you've earned
and cash out in SOL yourself. No invoices, no waiting on us.

Worth a look? Happy to set the code up and you decide after you see it pay.
```

Why each line is there:
- *"Not asking for a shoutout"* — separates us from the 40 agency DMs they got today.
- *"their auto-bets included"* — Orbit really does keep earning them money passively. It is unusual and true.
- *"cash out yourself"* — the differentiator. Do not bury it.
- *"you decide after you see it pay"* — a code costs us nothing; set it live and let the dashboard sell.

**Adjust per tier:** for `RP-06` (DeFi analyst) drop "degen" framing entirely and lead with *provably
fair + on-chain settlement + no custody*. For Kick streamers, ask for **one segment**, and say plainly
you know they have a casino deal and this does not conflict.

**Do not:** promise a rate you can raise later (§1), imply the launch token pays out, quote USDC, or
send the same message twice.

---

## 5. Before the first partner goes live — ops checklist

1. **Insert the partner row by hand.** Production ships with **no real partners**; `ReferralPartners`
   is a table and config never reaches prod. Needs `Code`, `NormalizedCode`, `DisplayName`,
   `SharePercentage`, `PayoutWallet`, `Active`.
2. **Get their Solana payout wallet in writing first.** `PayoutWallet` is UNIQUE (filtered on NOT NULL)
   — one wallet, one partner. `/partner` only works for the wallet on the row.
3. **Fund the dedicated `ReferralPayout` account.** Cash-out settles from it, never the treasury, and
   the solvency guard is pool-aware — an underfunded account blocks *every* partner's cash-out.
4. **Set the rate once.** See §1. Re-rating re-prices history.
5. **Send them `/partner` explicitly** — it is unlisted and in no menu. They cannot find it.
6. Turning a partner off (`Active = false`) stops *new* attribution only; their earned balance stays
   payable and they can still cash out. That is intended — do not treat it as a clawback.

---

## 6. Re-verification protocol

Everything here rots. Before using a row:

- **Follower counts and DM status change.** Re-open the profile; DM state is the presence of the
  **Message** button (`[data-testid="sendDMFromProfile"]`).
- **A bio affiliate code is the disqualifier to re-check first** — partners switch casinos.
- ⚠️ **X and Kick are unreadable without a signed-in browser.** `WebFetch` and plain web search return
  SEO affiliate spam for every query in this space. Use the browser tools. X's `min_faves:` search
  operator is what separates real accounts from airdrop-farming replies — without it the results are
  unusable.
- Kick follower counts render as an animated digit ticker: read them by regex over the page text
  (`/[\d.,]+\s*(mil|k)?\s*(seguidores|followers)/i`), not from the first lines of the card.
- Kick's category slug is **`/category/slots`** (`/categories/slots-casino` 404s).

### Correction log

- **2026-08-27** — created. Corrected two assumptions carried from `promotion-channels.md`: that large
  gambling streamers are approachable (§2 — they publish rival codes in-bio), and that its product
  facts still hold (USDC → **native SOL**, cut → **5%**).
