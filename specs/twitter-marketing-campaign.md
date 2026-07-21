# Space Stroker — "Twitter/X launch marketing campaign" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Create twitter marketing campaign](https://trello.com/c/gApr5z7v/76-create-twitter-marketing-campaign) (2026-07-21).
> Repos affected: **`monkify-meta` only** (this is a non-code content deliverable; no `monkify-backend` / `monkify-front` change).
>
> **Purpose:** Produce a ready-to-run, **$0-budget organic** Twitter/X launch campaign for the official
> **@SpaceStroker** account so the 3-person team can announce the **2026-07-23** crypto launch and pull in
> Solana players — an account bio, ready-to-paste tweet copy (pre-launch → launch day → first week), a
> relative-day posting schedule, and an organic growth playbook. This spec is the **blueprint + source of
> truth**; the implementer writes the final deliverable file.

---

## 1. Summary

This card ships **one markdown deliverable** — `monkify-meta/marketing/twitter-launch-campaign.md` — that a
non-marketer can execute by copy-pasting. It is **not code**: there is no backend, frontend, database,
endpoint, or test-suite change. The campaign is built entirely from the **live product truth** already on the
landing page (`monkify-lp`) and the game (`monkify-front`), and it stays inside hard content guardrails
because Space Stroker is a **real-money betting product** with a deliberately **token-free** stance.

| Aspect | This card |
| --- | --- |
| Deliverable | `monkify-meta/marketing/twitter-launch-campaign.md` (new `marketing/` folder) |
| Kind | Marketing **content** — bio + tweet copy + schedule + organic growth playbook + per-tweet media direction |
| Channel | Twitter/X only (Telegram is card **#77**; Discord out of scope) |
| Budget | **$0** — 100% organic, no paid promotion, no giveaway |
| Language | **English only** |
| Launch | **2026-07-23** = **T-0** (this spec written T-2) |
| Level | **Minor change** — content-only → **no integration test, no `features/<slug>.md` doc** (see §2) |

**What "done" is:** a single file whose five sections (bio, tweet copy, schedule, playbook, media direction)
satisfy all 8 acceptance criteria in §12 — every tweet ≤280 chars, the bio ≤160 chars, no raw app link in any
main tweet body, a relative-day schedule, and a clean content audit (no ROI promises, no fake metrics, no token
ticker/CA).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there). Most code constraints are **N/A** for a content card
and are marked so — do not invent code work.

- **Worktree-per-repo:** all work in `.worktrees/twitter-marketing-campaign/monkify-meta`; the `monkify-meta`
  hub stays on `main`. Branch `feature/twitter-marketing-campaign` (already created + pushed).
- **Level = Minor change (content):** **exempt** from the mandatory happy-path **integration test** and from a
  dedicated **`features/<slug>.md`** KB doc. There is **nothing to deploy** — the `monkify-implement-feature`
  Docker-stack deploy + "user verifies running containers" step is **N/A**; verification is the **content
  self-audit** in §11/§13 instead.
- **Unit tests:** **N/A** — no backend behaviour changes (`constraints.md` §3 does not apply).
- **EF mapping / migrations / hosts (`constraints.md` §4, §8):** **N/A** — no database or service change.
- **Secrets/config (`constraints.md` §1):** the deliverable must contain **no real credentials** — no account
  password, no API keys, no wallet/private keys, no contract address. The only "link" token is the flagged
  **`[PLAY_URL]`** placeholder.
- **Token-free integrity:** never publish a token **ticker or contract address**; never imply a token/airdrop is
  live. Mirror the LP's **"No token. Yet."** stance and its **anti-scam** line.
- **Responsible, honest promotion:** it is real-money betting → **no financial promises / guaranteed-returns /
  ROI / "get rich"** language; **no fabricated metrics or fake winners**; responsible-gaming-aware tone.
- **Vague-operator messaging (`constraints.md` §6):** N/A (no operator surfaces here), but the same honesty
  spirit applies to public copy.

---

## 3. Resolved product decisions

Carried from the refined card's **Decisions** + the refinement conversation (2026-07-21).

| # | Decision |
| --- | --- |
| 1 | **Deliverable = one file:** `monkify-meta/marketing/twitter-launch-campaign.md` (new `marketing/` folder, version-controlled, consistent with the `sales/` / `pitch/` precedent). |
| 2 | **Five required sections:** (1) Bio, (2) ready-to-paste tweet copy, (3) posting schedule, (4) organic growth playbook, (5) per-tweet media direction. |
| 3 | **Language: English only** (Solana/degen crypto Twitter is English-first). |
| 4 | **Tweet minimums:** ≥3 pre-launch teasers (T-2/T-1), one launch-day **thread** of ≥5 tweets (T-0), ≥5 first-week post-launch tweets (T+1…T+7). |
| 5 | **Link handling:** raw app URL is **kept out of the main tweet body** (X down-ranks external links) → **link in bio + drop it in the first reply**. All copy uses a flagged **`[PLAY_URL]`** placeholder (LP "Play now" URL is still a placeholder; domain undecided). |
| 6 | **Budget: $0 / fully organic.** No paid-promotion section. **No giveaway** / prize mechanics. |
| 7 | **Voice: grounded degen / arcade** — playful monkey-meme energy, Edson lore, "Gamble like an ape" / "BET ON THE MONKEY" — under the §6 guardrails. |
| 8 | **Positioning = the live LP truth** (§5): token-free, real **USDC**, provably-fair, instant on-chain **Solana** payouts, auto-refunds, Edson the space chimp, 3 game modes. **No token to buy.** |
| 9 | **Precedent research required** inside the playbook: a short scan of how comparable Solana betting/arcade/degen apps launched organically. |
| 10 | **Cold-start assumption:** @SpaceStroker is treated as **near-zero followers** → the playbook must cover cold-start seeding. *(If the account already has a following, the implementer can relax that emphasis and should note it.)* |
| 11 | **Media direction reuses existing assets only** (§5.3). Creating new images/GIFs/video is out of scope. |
| 12 | **Handle vs brand:** account handle is **`@SpaceStroker`** (one word — X handles can't contain spaces); the **display brand is "Space Stroker"** (two words). Keep both consistent with the shipped rebrand. |

---

## 4. Deliverable structure (`marketing/twitter-launch-campaign.md`)

The implementer creates this single file with the following top-level sections **in this order**. This is the
spec's core "what to build".

```
# Space Stroker — Twitter/X launch campaign

0. How to use this doc      — 3-line runbook: fill [PLAY_URL] in the bio, post per the schedule, links go in bio/first reply.
1. Account bio + profile    — the ≤160-char bio, display name, pinned-tweet choice, profile/banner asset picks.
2. Tweet copy               — every tweet, grouped by phase (T-2, T-1, T-0 thread, T+1…T+7), each paste-ready + labelled with its media pick + link note.
3. Posting schedule         — a table mapping every tweet in §2 to a relative day (T-2 … T+7) and a suggested slot.
4. Organic growth playbook  — the $0 distribution plan (see §8 of this spec for required subsections).
5. Media direction index    — table: each tweet slot → existing asset path(s) + a one-line "why".
6. Guardrails & pre-post checklist — the do-not list + a copy-paste self-audit the user runs before posting.
```

Each tweet in §2 of the deliverable is written as a labelled block so it can be pasted and tracked:

```
### T0-1 · Launch announce · media: edson-ship-typing.gif · link: bio + first reply
<tweet text, ≤280 chars>
↳ reply 1: Play now → [PLAY_URL]
```

---

## 5. Product facts & brand kit (source of truth for all copy)

All copy must be built from these live facts (from `monkify-lp/src/pages/index.astro`,
`components/GameModes.vue`, and the shipped `features/`). **Do not invent product claims.**

### 5.1 Brand, voice hooks, positioning

| Field | Value (verbatim-friendly) |
| --- | --- |
| Brand (display) | **Space Stroker** · wordmark **SPACE·STROKER** (STROKER in arcade orange `#ffa214`) |
| Handle | **@SpaceStroker** |
| Taglines | "**Gamble like an ape.**" · "**BET ON THE MONKEY**" · (Beast Mode) "**BET ON THE BEAST**" |
| One-liner | "Infinite monkey, infinite keystrokes. Drop real **USDC** on what Edson types next — first exact pick takes the pot." |
| Core mechanic | The **infinite monkey theorem**: Edson (an immortal space chimp) smashes keys forever; you **bet on what he types next**; the **first exact pick sweeps the pot**. |
| Currency | Real **USDC**, on **Solana**. No house coin, no presale, **no token to buy**. |
| Trust points | **Provably fair** (per-round seed) · **instant on-chain Solana payouts** · **auto-refund** of invalid bets. |
| Token stance | "**No token. Yet.**" A token *might* land later (ticker/date **TBD**); early players are first in line. **Anti-scam:** "Anyone selling a '$SPACE STROKER' today is not us." |
| Team | 3-person crew — **Bidi** (dev), **Ryugaki** (dev), **Bolso** (art & design). Passion-project framing ("3 friends built this"). |
| Community | "**Join the gang**" — the lobby's open; talk trash and call Edson's next keystrokes. |
| App link | **`[PLAY_URL]`** placeholder (real value TBD; LP uses `https://PLACEHOLDER-app-url/space-stroker/game`). Never hard-code a guessed domain. |

### 5.2 The three game modes (verbatim framing from the LP)

| Mode | Tag | Blurb |
| --- | --- | --- |
| **QUICK RACES** | 6 LETTERS · 6 NUMBERS | Call a 6-character run and pray. Edson hammers the keys, and the first sequence he spells out exactly scoops the pot. |
| **BEAST MODE** | BET ON THE BEAST | No typing required. Tap a beast — rabbit, monkey, jaguar, falcon — and the first one Edson spells out wins the whole pot. |
| **LOTTERIES** | EVERY 4H · DAILY DRAW | Grab a ticket, the house seeds the pot on top, and you wait for the bell. A draw every 4 hours, plus one fat daily. |

### 5.3 Existing media assets (media direction reuses these — do not create new art)

Primary source `monkify-lp/src/assets/` (also mirrored in the game app `monkify-front/src/assets`):

- **Hero / signature:** `edson-ship-typing.gif` (Edson typing in his flying saucer) ⭐, `edson.png` (still).
- **Gameplay sprites:** `game/typing.gif`, `game/typing2.gif`, `game/typing3.gif`, `game/idle.gif`, `game/call.gif`.
- **Arcade console:** `game/keyboard.png`, `game/leverup.png`, `game/leverdown.png`, `game/lever_to_up.gif`, `game/lever_to_down.gif`.
- **Modes:** `cards/beastmode.gif`, `cards/beastmode-poster.png`, `cards/a.png`, `cards/b.png`, `cards/c.png`.
- **Coin / money:** `monk-coin-main.png`, `monk-coin.png`.
- **Edson's story (great for a lore thread):** `backstory/part1.png` … `part6.png` (+ `mobile1..6.png`) ⭐.
- **Space elements:** `elements/comet.gif`, `elements/rocket.gif`, `elements/galaxy.png`, `elements/earth.png`, `elements/saturn.png`, `elements/blackhole.png`, `elements/star1..8.gif`.
- **Team avatars:** `team/BidiUser.png`, `team/RyugakiUser.png`, `team/BolsoUser.png`.

> **Format caveat (note in the deliverable, don't solve here):** these are `.png`/`.gif` source assets. X often
> renders short **`.mp4`** better than large GIFs; the media-direction index names the asset to use — actually
> exporting/converting a post-ready file is the user's step at post time, **not** part of this card.

### 5.4 Edson's story (for the T+ lore thread — summarised, 6 beats)

Brazilian circus act, "the monkey in a suit who types on a typewriter" → governments blow billions to shoot him
into space to type one super-long password → supernova stardust makes him **immortal**; he types for centuries,
his head literally **kaboom**s, he keeps going → millennia later Earth is dust and he's **forgotten the
password** → alien civilizations tune in and **gamble on his next line of gibberish** → he just keeps typing.
(Maps 1:1 to `backstory/part1..6.png`.)

---

## 6. Voice, tone & content guardrails

**Voice:** grounded degen / arcade. Playful, meme-literate, self-aware; leans on Edson lore and "gamble like an
ape". Confident but **honest** — the humour comes from the premise (betting on a monkey), never from fake hype.

**HARD guardrails — every tweet and the bio must obey (this is auditable in §11):**

- ❌ **No financial promises.** No "guaranteed", "risk-free", ROI/%/multiplier promises, "easy money", "get rich",
  "you will win". Betting language is fine ("bet", "call it", "sweep the pot"); *outcome* promises are not.
- ❌ **No fabricated metrics / social proof.** No invented player counts, pot sizes, winners, or testimonials.
  Real numbers may be posted **only after they actually happen** (see T+ "social proof" slot).
- ❌ **No token ticker or contract address**, ever. Never imply a token/airdrop is live or buyable. Where the
  topic comes up, use the **"No token. Yet." + anti-scam** framing.
- ⚠️ **Responsible-gaming aware.** It's real-money, 18+ betting. Keep a light "bet what you can afford to lose"
  sensibility; never target minors; don't pressure.
- ❌ **No raw app link in a main tweet body** — link lives in the **bio** and the **first reply** (§3 decision 5).
  Use the `[PLAY_URL]` placeholder in the doc.
- ❌ **No unshipped features / no roadmap-as-fact.** Only promote what's live on launch day (§5). Future items
  (a possible token) stay explicitly "maybe / later".
- ✅ **Consistent brand:** display "Space Stroker", handle @SpaceStroker, USDC-on-Solana, provably-fair.

---

## 7. Tweet content plan (blueprint the implementer fills in)

The implementer writes the **final** paste-ready copy; this spec fixes the **slots, angle, required elements, and
media** so nothing is guessed. Dates: **today = T-2 = 2026-07-21**, **launch = T-0 = 2026-07-23**. Every tweet
≤280 chars; links per §3 decision 5.

### 7.1 Pre-launch teasers (≥3 total across T-2 / T-1)

| Slot | Day | Angle / purpose | Must include | Media |
| --- | --- | --- | --- | --- |
| PRE-1 | T-2 | **Cold intro / mystery** — "who is Edson?" hook; set the account voice; **no hard sell**. | Edson + intrigue; "launching 07/23" teaser optional | `edson-ship-typing.gif` |
| PRE-2 | T-2/T-1 | **Mechanic reveal** — infinite monkey theorem → "bet on what the monkey types". | the core mechanic in one line; USDC-on-Solana | `game/typing2.gif` |
| PRE-3 | T-1 | **Countdown / differentiator** — "24h. real USDC, provably fair, instant payouts, no token." | date/countdown + ≥2 trust points | `monk-coin-main.png` or fair-play framing |
| PRE-4 (opt) | T-1 | **Mode tease** — "pick your table" (QUICK RACES / BEAST MODE / LOTTERIES). | ≥1 mode named | `cards/beastmode.gif` |

Also specify the **bio + pinned-tweet** go live at T-2 (§ deliverable §1).

### 7.2 Launch-day thread — T-0 (2026-07-23), **≥5 tweets**

| # | Purpose | Must include |
| --- | --- | --- |
| T0-1 | **We're live** announce (thread head). | "live now" + one-liner; link **only** in bio/first reply |
| T0-2 | **What it is** — infinite monkey theorem + Edson, one-liner. | core mechanic; USDC on Solana |
| T0-3 | **How it works** — 3 steps: type your guess → pull the lever with USDC → Edson goes off, first exact pick sweeps the pot. | the 3 steps |
| T0-4 | **The 3 modes** — QUICK RACES / BEAST MODE / LOTTERIES (use §5.2 framing). | all 3 modes |
| T0-5 | **Why it's fair** — provably-fair per-round seed, instant on-chain Solana payouts, auto-refunds; **no house coin, no presale — "No token. Yet."** + anti-scam. | 3 trust points + token-free line |
| T0-6 | **CTA + who we are** — "3 friends built this, come play"; link in bio; "Join the gang". | CTA + first reply carries `[PLAY_URL]` |

### 7.3 First-week sustain — T+1 … T+7, **≥5 tweets**

| Slot | Day (suggested) | Angle | Media |
| --- | --- | --- | --- |
| POST-1 | T+1 | **Edson lore thread** (6 beats → thread). | `backstory/part1..6.png` |
| POST-2 | T+2 | **BEAST MODE spotlight** (rabbit/monkey/jaguar/falcon, no typing). | `cards/beastmode.gif` |
| POST-3 | T+3 | **LOTTERIES spotlight** (draw every 4h + daily; house seeds the pot). | `cards/c.png` |
| POST-4 | T+4 | **Provably-fair explainer** — how to verify a round (point to the in-app verifier). | `game/keyboard.png` |
| POST-5 | T+5 | **Engagement / reply-bait** — "what does Edson type next?" poll or call-out. | `game/idle.gif` |
| POST-6 | T+6–7 | **Real social proof _if it exists_** (first winners / biggest pot) — **real numbers only**; else reframe as an invite. | screenshot / `edson.png` |

---

## 8. Organic growth playbook (required subsections of deliverable §4)

$0 budget. The implementer must produce concrete, **currently-real** tactics (research at implement time via web
search where "currently-real" is needed — named communities and handles change). Required subsections:

1. **Account setup & optimisation** — handle @SpaceStroker; display name "Space Stroker"; **bio with `[PLAY_URL]`
   as the link-in-bio**; pinned tweet = the launch thread; profile pic + banner picks from §5.3; a one-line
   "what we are" so a cold visitor gets it in 2 seconds.
2. **Cold-start seeding (near-zero followers).** The 3 crew + their circles post/RT from personal accounts,
   seed the first replies, and prime the launch thread so it doesn't go out to an empty room. Concrete "do this
   in the first hour" list.
3. **Where the audience is** — name concrete **Solana** venues to show up in: Solana degen/gaming Twitter
   circles, on-chain-gaming & casino communities, relevant Telegram/Discord hubs, subreddits (e.g. r/solana),
   Farcaster/SocialFi. *(Implementer fills current, real names/links — do not ship stale placeholders.)*
4. **Reply / quote-tweet strategy** — earn reach by adding genuine, funny (Edson-voiced) replies to larger
   Solana accounts, games, and KOLs; quote-tweet relevant threads. **Not** spam / copy-paste shilling.
5. **Hashtags** — sparing (1–2), e.g. `#Solana`, `#SolanaGaming`; guidance on when they help vs hurt.
6. **Best times to post** — crypto-Twitter is busiest around the US/EU overlap; give concrete windows and a
   "test and watch analytics" note.
7. **Free KOL / micro-influencer outreach** — a short DM template offering early access / a look, **no paid
   promo**; target small Solana-gaming creators first.
8. **Ongoing cadence** — beyond launch week: memes, Edson lore, polls ("call his next keystrokes"), "gm",
   winner reposts (real only). Keep it a loop, not a one-off.
9. **Precedent scan (research)** — 3–5 concrete lessons from how comparable **Solana** betting/arcade/degen apps
   ran their organic launches (what worked, what to avoid). Card asks for this explicitly.
10. **Platform-safety notes** — X's stance on gambling promotion (links in bio/replies, **no paid gambling
    ads** — moot at $0), plus a one-line responsible-gaming reminder and the token-free/anti-scam rule.

---

## 9. Out of scope

- **Telegram** campaign — separate card **#77** ("Create Telegram marketing campaign").
- **Discord** and any non-Twitter channel.
- **Any paid advertising / promotion** (campaign is $0 organic).
- **Any giveaway / prize / free-credit mechanic.**
- **Creating new images / GIFs / video**, or exporting post-ready media files (media direction **references
  existing assets only**; final export is the user's at post time).
- **LP or game-app code changes** (no `monkify-lp` / `monkify-front` / `monkify-backend` edits).
- **Actually posting the tweets** and running the account — this card produces the content + plan; posting is
  the user's action.
- **Securing the production app domain / URL** — the copy stays link-agnostic via `[PLAY_URL]`.
- **Backend integration test + a `features/<slug>.md` doc** — waived (Minor change, content-only).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None** — no code change.

### 10.2 Frontend (`monkify-front` / `monkify-lp`)

**None** — read-only source of product facts + assets (§5). No edits.

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `marketing/twitter-launch-campaign.md` | **NEW** — the campaign deliverable (§4 structure). *Created by `/monkify-implement-feature`.* |
| 2 | `specs/twitter-marketing-campaign.md` | **NEW** — this spec. *Created by this skill.* |
| 3 | `INDEX.md` | Add the spec row + "Feature currently in flight" entry. *Updated by this skill.* |

---

## 11. Verification checklist (replaces "unit tests" — content QA)

No test project applies. Instead, `/monkify-implement-feature` verifies the deliverable against these
auditable checks (each maps to an acceptance criterion in §12):

| Check | How | AC |
| --- | --- | --- |
| File + 5 sections present | Open `marketing/twitter-launch-campaign.md`; confirm bio / tweet copy / schedule / playbook / media-direction sections. | 1 |
| Bio ≤160 chars, on-message, clean | Char-count the bio; confirm token-free USDC/Solana framing; grep for ticker/`$`/"guaranteed". | 2 |
| Tweet counts + lengths | Count ≥3 pre-launch, ≥5 launch-thread, ≥5 post-launch; char-count each ≤280. | 3 |
| Link discipline | Grep tweet bodies for `http`/`.com`/`[PLAY_URL]` in the **body**; confirm links are bio/first-reply only. | 4 |
| Relative-day schedule | Confirm the schedule table keys every tweet to T-2…T+7, not fixed dates. | 5 |
| Playbook concreteness | Confirm named Solana communities, reply/quote tactics, hashtags, post times, KOL outreach, **and** the precedent scan. | 6 |
| Content audit | Grep the whole file for banned terms (guaranteed, risk-free, ROI, "get rich", ticker/`$SPACE`, CA-like strings); confirm anti-scam + responsible-gaming lines exist. | 7 |
| Media direction real | Every media reference resolves to a real path under §5.3; no "create new image" asks. | 8 |

**Suggested self-audit command** (run in the `monkify-meta` worktree):
`grep -niE "guaranteed|risk-free|\broi\b|get rich|1000x|airdrop live|\$SPACE" marketing/twitter-launch-campaign.md`
→ expect no offending matches (mentions inside the *anti-scam* / guardrail lines are acceptable, judged in context).

---

## 12. Acceptance criteria

Copied from the Trello **Acceptance Criteria** checklist (Given / When / Then):

1. Given the deliverable, when opened, then `monkify-meta/marketing/twitter-launch-campaign.md` exists with
   distinct sections for: bio, ready-to-paste tweet copy, posting schedule, organic growth playbook, and
   per-tweet media direction.
2. Given the @SpaceStroker bio, when read, then it is ≤160 characters, names the product as token-free USDC
   provably-fair Solana betting, and contains no token ticker/contract address and no guaranteed-returns claim.
3. Given the tweet copy, when counted, then it includes at least 3 pre-launch teasers (T-2/T-1), one launch-day
   thread of at least 5 tweets (T-0), and at least 5 first-week post-launch tweets (T+1..T+7), each ≤280
   characters and paste-ready.
4. Given any tweet that references the game, when inspected, then the raw app URL is not in the main tweet body
   (link-in-bio / first-reply pattern is used) and any app link is written as the placeholder `[PLAY_URL]`.
5. Given the posting schedule, when read, then every piece of tweet copy is mapped to a relative day
   (T-2 .. T+7) rather than a fixed calendar date, so the plan survives a launch-date slip.
6. Given the organic growth playbook, when read, then it lists concrete $0 tactics (named Solana
   communities/hubs, reply/quote-tweet strategy, hashtags, best post times, free KOL outreach) and includes a
   short precedent scan of how comparable Solana betting/arcade/degen apps launched.
7. Given the whole deliverable, when audited, then it contains no financial-return promise or guaranteed-returns
   language, no fabricated metrics/engagement, and no token ticker or contract address, and it includes a
   responsible-gaming-aware note plus an anti-scam ("no token yet") line.
8. Given the per-tweet media direction, when checked, then every media suggestion references an existing
   Edson/arcade asset from monkify-lp or monkify-front (no request to create new images/GIFs/video).

---

## 13. Manual QA

1. Open `marketing/twitter-launch-campaign.md`; skim the 6 sections (§4) exist and read cleanly.
2. Paste 3–4 representative tweets into any character counter → each ≤280; paste the bio → ≤160.
3. Run the §11 self-audit grep → confirm no banned-term matches outside the guardrail/anti-scam lines.
4. Spot-check 3 media references against the real asset paths in §5.3 (e.g. `ls monkify-lp/src/assets/edson-ship-typing.gif`).
5. Confirm the schedule table is relative-day (T-2…T+7) and that every tweet in §2 appears in it.
6. Sanity-read for voice: does it sound like grounded degen/arcade, honest, and on-brand ("Space Stroker",
   @SpaceStroker, USDC on Solana, provably fair, no token)?

---

## 14. Trello / pipeline

- **Source card:** [#76 Create twitter marketing campaign](https://trello.com/c/gApr5z7v/76-create-twitter-marketing-campaign) — list **Specs created**; the **Implementation** section on the card carries branch + spec + worktree + level.
- **Worktree:** `.worktrees/twitter-marketing-campaign/monkify-meta` · **Branch:** `feature/twitter-marketing-campaign`.
- **Next:** `/monkify-implement-feature Create twitter marketing campaign` to write the deliverable (**no plan-to-code / Docker deploy / integration test** — content-only; verify via §11). Then `/monkify-ship-feature Create twitter marketing campaign` to merge to `main` (**no `features/<slug>.md` doc** — Minor change; the KB sync just adds the file + keeps INDEX current).

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-21 | Initial spec from refined card #76. Content deliverable (Twitter/X launch campaign); adapted the code-oriented template to a marketing brief — code sections (data model, migrations, unit tests, backend/frontend behaviour) marked N/A; added deliverable-structure, brand-kit/source-of-truth, voice/guardrails, tweet blueprint, growth-playbook outline, and content-QA verification. Grounded in `monkify-lp` (`index.astro`, `GameModes.vue`) + `features/`. |
