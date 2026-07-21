# Space Stroker — "Telegram launch marketing campaign" — Implementation Spec

> **Status: Ready for implementation** — refined via Trello card
> [Create Telegram marketing campaign](https://trello.com/c/LTrjTgdy/77-create-telegram-marketing-campaign) (2026-07-21).
> Repos affected: **`monkify-meta` only** (this is a non-code content deliverable; no `monkify-backend` / `monkify-front` change).
>
> **Purpose:** Produce a ready-to-run, **$0-budget organic** Telegram launch campaign for the official
> **Space Stroker community group** so the 3-person team can announce the **2026-07-23** crypto launch and give
> Solana players a **community/retention home** to convert into — a group identity (description + pinned welcome),
> ready-to-paste message copy (pre-launch → launch day → first week), a relative-day posting schedule, an organic
> growth playbook, per-message media direction, and a Telegram-specific **group setup + moderation & anti-scam**
> playbook. This spec is the **blueprint + source of truth**; the implementer writes the final deliverable file.
> It is the **sibling** of the Twitter/X campaign ([#76](https://trello.com/c/gApr5z7v),
> `specs/twitter-marketing-campaign.md`) — same product truth and guardrails, adapted to Telegram's community
> mechanics; **not** a duplicate.

---

## 1. Summary

This card ships **one markdown deliverable** — `monkify-meta/marketing/telegram-launch-campaign.md` — that a
non-marketer can execute by copy-pasting. It is **not code**: there is no backend, frontend, database, endpoint,
or test-suite change. The campaign is built entirely from the **live product truth** already on the landing page
(`monkify-lp`) and the game (`monkify-front`) — the same source of truth as the Twitter card — and it stays inside
hard content guardrails because Space Stroker is a **real-money betting product** with a deliberately **token-free**
stance. Because Telegram is a **community** platform (not a broadcast feed), the deliverable adds a sixth section on
**running a safe group** — crypto Telegram is a heavy scam-impersonation target, so anti-scam is a first-class
requirement here, not an afterthought.

| Aspect | This card |
| --- | --- |
| Deliverable | `monkify-meta/marketing/telegram-launch-campaign.md` (in the existing `marketing/` folder from #76) |
| Kind | Marketing **content + community-ops** — group identity + message copy + schedule + growth playbook + media direction + group-setup/moderation/anti-scam |
| Channel | **Telegram only** (Twitter/X is card **#76**; Discord out of scope) |
| Presence model | **A single open community group** (announcements + chat in one), **not** a separate broadcast channel + discussion group |
| Cross-platform role | **Telegram is the retention hub; Twitter/X (#76) is top-of-funnel that drives into it** (funnel tactics in §8) — but the plan stands alone if X slips |
| Budget | **$0** — 100% organic, no paid promotion, no giveaway |
| Language | **English only** |
| Launch | **2026-07-23** = **T-0** (this spec written T-2) |
| Level | **Minor change** — content-only → **no integration test, no `features/<slug>.md` doc** (see §2) |

**What "done" is:** a single file whose **six** sections (group description + pinned welcome, message copy,
posting schedule, organic growth playbook, per-message media direction, and group setup + moderation & anti-scam)
satisfy all 8 acceptance criteria in §12 — ≥3 pre-launch teasers, a launch-day sequence of ≥5 messages, ≥5
first-week messages, `[PLAY_URL]` handled as direct-link + inline button + pin, a relative-day schedule, a concrete
$0 playbook incl. X→Telegram funnel + precedent scan, a real moderation/anti-scam section, and a clean content audit
(no ROI promises, no fake metrics, no token ticker/CA).

---

## 2. Constraints (MANDATORY)

From `monkify-meta/constraints.md` (full list there). Most code constraints are **N/A** for a content card and are
marked so — do not invent code work.

- **Worktree-per-repo:** all work in `.worktrees/telegram-marketing-campaign/monkify-meta`; the `monkify-meta` hub
  stays on `main`. Branch `feature/telegram-marketing-campaign` (already created + pushed).
- **Level = Minor change (content):** **exempt** from the mandatory happy-path **integration test** and from a
  dedicated **`features/<slug>.md`** KB doc. There is **nothing to deploy** — the `monkify-implement-feature`
  Docker-stack deploy + "user verifies running containers" step is **N/A**; verification is the **content
  self-audit** in §11/§13 instead.
- **Unit tests:** **N/A** — no backend behaviour changes (`constraints.md` §3 does not apply).
- **EF mapping / migrations / hosts (`constraints.md` §4, §8):** **N/A** — no database or service change.
- **Secrets/config (`constraints.md` §1):** the deliverable must contain **no real credentials** — no account
  password, no bot API token, no wallet/private keys, no contract address. The only "link" tokens are the flagged
  **`[PLAY_URL]`** (app) and **`[GROUP_URL]`** (the public group link) placeholders. A moderation **bot token** is
  referenced only as a placeholder (`[BOT_TOKEN]`); never write a real one.
- **Token-free integrity:** never publish a token **ticker or contract address**; never imply a token/airdrop is
  live. Mirror the LP's **"No token. Yet."** stance and its **anti-scam** line. On Telegram this is doubly load-bearing
  because impersonators will post fake CAs in the group (see §8.5).
- **Responsible, honest promotion:** it is real-money betting → **no financial promises / guaranteed-returns / ROI /
  "get rich"** language; **no fabricated metrics or fake winners**; responsible-gaming-aware tone.
- **Vague-operator messaging (`constraints.md` §6):** N/A (no operator surfaces here), but the same honesty spirit
  applies to public copy.

---

## 3. Resolved product decisions

Carried from the refined card #77 **Decisions** + the refinement conversation (2026-07-21). Product-truth decisions
(voice, positioning, token-free, media-reuse, launch date, cold-start) are **inherited from the Twitter spec** and
kept identical for brand consistency; the **Telegram-specific** ones are called out.

| # | Decision |
| --- | --- |
| 1 | **Deliverable = one file:** `monkify-meta/marketing/telegram-launch-campaign.md` (existing `marketing/` folder, alongside `twitter-launch-campaign.md`, version-controlled). |
| 2 | **Six required sections** (Telegram adds a 6th beyond the Twitter five): (1) group description + pinned welcome, (2) ready-to-paste message copy, (3) posting schedule, (4) organic growth playbook, (5) per-message media direction, (6) **group setup + moderation & anti-scam**. |
| 3 | **Presence model = a single open community group** (announcements + chat in one). **Not** a separate broadcast channel + discussion group. All copy + setup target that one group. *(Telegram-specific.)* |
| 4 | **Link handling** (Telegram-specific — differs from X): Telegram does **not** down-rank external links, so the app link goes **directly in the message body** *and* as an **inline "PLAY NOW" URL button** *and* in the **pinned message**. All via the flagged **`[PLAY_URL]`** placeholder. **Inline buttons require a bot or an admin-pinned bot message** — the doc must state the delivery method and give a **plain-link fallback** for ordinary member messages (see §6/§8.5). |
| 5 | **Cross-platform role:** **Telegram is the community/retention hub; Twitter/X (#76) is top-of-funnel reach that funnels into it.** The playbook includes explicit **X → Telegram funnel** tactics and cross-references #76. Must still stand up if the X campaign slips. *(Telegram-specific.)* |
| 6 | **Message minimums:** ≥3 pre-launch teasers (T-2/T-1), one launch-day **announcement sequence of ≥5 messages** (T-0, the Telegram analogue of the Twitter thread; T0-1 is **pinned**), ≥5 first-week post-launch messages (T+1…T+7). |
| 7 | **Budget: $0 / fully organic.** No paid promotion / cross-promo spend. **No giveaway** / prize / join-and-invite mechanics. |
| 8 | **Language: English only** (Solana/degen crypto Telegram is English-first). |
| 9 | **Voice: grounded degen / arcade** — playful monkey-meme energy, Edson lore, "Gamble like an ape" / "BET ON THE MONKEY" — under the §6 guardrails. |
| 10 | **Positioning = the live LP truth** (§5): token-free, real **USDC**, provably-fair, instant on-chain **Solana** payouts, auto-refunds, Edson the space chimp, 3 game modes. **No token to buy.** |
| 11 | **Precedent research required** inside the playbook: a short scan of how comparable Solana betting/arcade/degen apps ran their **Telegram** community launch (group vs channel, captcha norm, mod-bot stack, raid culture). |
| 12 | **Cold-start assumption:** the group starts at **near-zero members** → the playbook must cover cold-start seeding + the X→Telegram funnel. *(If the group already has members, the implementer relaxes that emphasis and notes it.)* |
| 13 | **Media direction reuses existing assets only** (§5.3). Creating new images/GIFs/video — or a **custom Telegram sticker pack** — is out of scope. (Telegram renders `.gif`/`.mp4` natively, so no export step is needed, unlike X.) |
| 14 | **Safety-first (Telegram-specific):** §6 must specify a **moderation bot stack** (e.g. Rose / Combot + a captcha/anti-raid bot such as Safeguard), **join captcha / anti-bot gating**, and **ready-to-pin anti-scam safety rules** ("admins never DM first", "there is no token / contract address", "we never ask for your seed phrase"). |
| 15 | **Group handle vs brand:** display brand is **"Space Stroker"** (two words); the group's public **username/link is `[GROUP_URL]`** (TBD — the user claims an available one, e.g. `t.me/SpaceStroker` or `t.me/SpaceStrokerGang`). Never hard-code a guessed handle; keep it a flagged placeholder like `[PLAY_URL]`. |

---

## 4. Deliverable structure (`marketing/telegram-launch-campaign.md`)

The implementer creates this single file with the following top-level sections **in this order**. This is the
spec's core "what to build". Sections 1–6 are the six **required** sections (AC #1).

```
# Space Stroker — Telegram launch campaign

0. How to use this doc      — short runbook: create the group, claim [GROUP_URL], set the mod bot + captcha,
                              paste the description + pinned welcome, post per the schedule, links go in-body +
                              PLAY NOW button + pin.
1. Group identity           — the ≤255-char description (bio-equivalent), display name, public link [GROUP_URL],
   + pinned welcome           group photo pick, the pinned WELCOME message (with a PLAY NOW button), and the
                              pinned SAFETY notice. (Required section 1.)
2. Message copy             — every message, grouped by phase (T-2, T-1, T-0 launch sequence, T+1…T+7), each
                              paste-ready + labelled with its media pick + link/button note. (Required section 2.)
3. Posting schedule         — a table mapping every message in §2 to a relative day (T-2 … T+7) and a slot.
                              (Required section 3.)
4. Organic growth playbook  — the $0 distribution plan incl. cold-start seeding, the X→Telegram funnel, where the
                              audience is, precedent scan (see §8 of this spec for required subsections).
                              (Required section 4.)
5. Media direction index    — table: each message slot → existing asset path(s) + a one-line "why".
                              (Required section 5.)
6. Group setup, moderation  — the Telegram-specific ops section: group-creation checklist, mod-bot stack + captcha,
   & anti-scam                permissions/slow-mode, ready-to-pin anti-scam rules, content guardrails + pre-post
                              self-audit. (Required section 6 — see §8.5 of this spec.)
7. Open items before you launch — the small TBDs ([PLAY_URL], [GROUP_URL], bot token, starting size). (Housekeeping.)
```

Each message in §2 of the deliverable is written as a labelled block so it can be pasted and tracked. Telegram has
**no 280-char cap**, so messages may be a little longer than tweets and use light Markdown (bold/italics), emoji,
and inline links — but keep them **skimmable**:

```
### T0-1 · Launch announce (PIN THIS) · media: edson-ship-typing.gif · link: in-body [PLAY_URL] + 🎮 PLAY NOW button
<message text — may use *bold*, line breaks, emoji; [PLAY_URL] appears in the body>
[ 🎮 PLAY NOW → [PLAY_URL] ]   ← inline URL button (posted via the mod/welcome bot; plain-link fallback if no bot)
```

---

## 5. Product facts & brand kit (source of truth for all copy)

Identical product truth to the Twitter spec (single source of truth). All copy must be built from these live facts
(from `monkify-lp/src/pages/index.astro`, `components/GameModes.vue`, and the shipped `features/`). **Do not invent
product claims.**

### 5.1 Brand, voice hooks, positioning

| Field | Value (verbatim-friendly) |
| --- | --- |
| Brand (display) | **Space Stroker** · wordmark **SPACE·STROKER** (STROKER in arcade orange `#ffa214`) |
| Group link | **`[GROUP_URL]`** — the public group's `t.me/…` link (TBD; user claims an available username). Never hard-code a guessed handle. |
| Taglines | "**Gamble like an ape.**" · "**BET ON THE MONKEY**" · (Beast Mode) "**BET ON THE BEAST**" |
| One-liner | "Infinite monkey, infinite keystrokes. Drop real **USDC** on what Edson types next — first exact pick takes the pot." |
| Core mechanic | The **infinite monkey theorem**: Edson (an immortal space chimp) smashes keys forever; you **bet on what he types next**; the **first exact pick sweeps the pot**. |
| Currency | Real **USDC**, on **Solana**. No house coin, no presale, **no token to buy**. |
| Trust points | **Provably fair** (per-round seed) · **instant on-chain Solana payouts** · **auto-refund** of invalid bets. |
| Token stance | "**No token. Yet.**" A token *might* land later (ticker/date **TBD**); early players are first in line. **Anti-scam:** "Anyone selling a '$SPACE STROKER' today — or posting a contract address in here — is not us." |
| Team | 3-person crew — **Bidi** (dev), **Ryugaki** (dev), **Bolso** (art & design). Passion-project framing ("3 friends built this"). |
| Community | "**Join the gang**" — the lobby's open; talk trash and call Edson's next keystrokes. **This is the whole point of the Telegram group.** |
| App link | **`[PLAY_URL]`** placeholder (real value TBD; LP uses `https://PLACEHOLDER-app-url/space-stroker/game`). Never hard-code a guessed domain. |

### 5.2 The three game modes (verbatim framing from the LP)

| Mode | Tag | Blurb |
| --- | --- | --- |
| **QUICK RACES** | 6 LETTERS · 6 NUMBERS | Call a 6-character run and pray. Edson hammers the keys, and the first sequence he spells out exactly scoops the pot. |
| **BEAST MODE** | BET ON THE BEAST | No typing required. Tap a beast — rabbit, monkey, jaguar, falcon — and the first one Edson spells out wins the whole pot. |
| **LOTTERIES** | EVERY 4H · DAILY DRAW | Grab a ticket, the house seeds the pot on top, and you wait for the bell. A draw every 4 hours, plus one fat daily. |

### 5.3 Existing media assets (media direction reuses these — do not create new art)

Primary source `monkify-lp/src/assets/` (also mirrored in the game app `monkify-front/src/assets`). **All paths
below were verified present (2026-07-21).**

- **Hero / signature:** `edson-ship-typing.gif` (Edson typing in his flying saucer) ⭐, `edson.png` (still).
- **Gameplay sprites:** `game/typing.gif`, `game/typing2.gif`, `game/typing3.gif`, `game/idle.gif`, `game/call.gif`.
- **Arcade console:** `game/keyboard.png`, `game/leverup.png`, `game/leverdown.png`, `game/lever_to_up.gif`, `game/lever_to_down.gif`.
- **Modes:** `cards/beastmode.gif`, `cards/beastmode-poster.png`, `cards/a.png`, `cards/b.png`, `cards/c.png`.
- **Coin / money:** `monk-coin-main.png`, `monk-coin.png`.
- **Edson's story (great for a lore drop):** `backstory/part1.png` … `part6.png` (+ `mobile1..6.png`) ⭐.
- **Space elements:** `elements/comet.gif`, `elements/rocket.gif`, `elements/galaxy.png`, `elements/earth.png`, `elements/saturn.png`, `elements/blackhole.png`, `elements/star1..8.gif`.
- **Team avatars:** `team/BidiUser.png`, `team/RyugakiUser.png`, `team/BolsoUser.png`.

> **Telegram format note (a plus over X):** Telegram renders `.gif` and `.mp4` **natively and inline**, so — unlike
> the Twitter campaign — there is **no GIF→MP4 export step**; the source GIFs above can be posted as-is (Telegram
> converts GIFs to silent looping MP4 automatically). A **group photo** must be a **static square image** (use
> `edson.png` or a crop). **Custom sticker packs are out of scope** (that's new art) — reuse the GIFs as animations.

### 5.4 Edson's story (for the T+ lore drop — summarised, 6 beats)

Brazilian circus act, "the monkey in a suit who types on a typewriter" → governments blow billions to shoot him into
space to type one super-long password → supernova stardust makes him **immortal**; he types for centuries, his head
literally **kaboom**s, he keeps going → millennia later Earth is dust and he's **forgotten the password** → alien
civilizations tune in and **gamble on his next line of gibberish** → he just keeps typing.
(Maps 1:1 to `backstory/part1..6.png`.)

---

## 6. Voice, tone & content guardrails

**Voice:** grounded degen / arcade. Playful, meme-literate, self-aware; leans on Edson lore and "gamble like an ape".
Confident but **honest** — the humour comes from the premise (betting on a monkey), never from fake hype. On Telegram
the register is **more conversational and community-native** than X (it's a chat, not a billboard): "gm", banter,
polls, reacting to members — but the same hard guardrails apply to every admin/announcement message.

**HARD guardrails — every message and the group description/pins must obey (auditable in §11):**

- ❌ **No financial promises.** No "guaranteed", "risk-free", ROI/%/multiplier promises, "easy money", "get rich",
  "you will win". Betting language is fine ("bet", "call it", "sweep the pot"); *outcome* promises are not.
- ❌ **No fabricated metrics / social proof.** No invented member counts, pot sizes, winners, or testimonials. Real
  numbers may be posted **only after they actually happen** (see T+ "social proof" slot).
- ❌ **No token ticker or contract address**, ever. Never imply a token/airdrop is live or buyable. Use the
  **"No token. Yet." + anti-scam** framing. On Telegram, **pin** this and have mods **auto-remove** any CA/ticker
  posted by members (§8.5).
- ⚠️ **Responsible-gaming aware.** It's real-money, 18+ betting. Keep a light "bet what you can afford to lose"
  sensibility; never target minors; don't pressure.
- ✅ **Link handling (Telegram):** `[PLAY_URL]` **may** appear directly in admin/announcement message bodies, as an
  inline **PLAY NOW** button, and in the pin. (This is the opposite of the X rule, where links stay out of the body.)
  Only **official** links go in announcements; members posting links are gated by moderation (§8.5).
- ❌ **No unshipped features / no roadmap-as-fact.** Only promote what's live on launch day (§5). Future items (a
  possible token) stay explicitly "maybe / later".
- ✅ **Consistent brand:** display "Space Stroker", USDC-on-Solana, provably-fair; group link `[GROUP_URL]`.

---

## 7. Message content plan (blueprint the implementer fills in)

The implementer writes the **final** paste-ready copy; this spec fixes the **slots, angle, required elements, and
media** so nothing is guessed. Dates: **today = T-2 = 2026-07-21**, **launch = T-0 = 2026-07-23**. Links per §3
decision 4 (in-body + PLAY NOW button + pin). Messages are posted **in the group** by an admin (or the bot for
button-bearing ones).

### 7.1 Pre-launch teasers (≥3 total across T-2 / T-1)

| Slot | Day | Angle / purpose | Must include | Media |
| --- | --- | --- | --- | --- |
| PRE-1 | T-2 | **Doors-opening intro** — "the lobby opens 07/23; who is Edson?"; set the group voice; soft. | Edson + intrigue; "opens 07/23" teaser | `edson-ship-typing.gif` |
| PRE-2 | T-2/T-1 | **Mechanic reveal** — infinite monkey theorem → "bet on what the monkey types". | the core mechanic in one line; USDC-on-Solana | `game/typing2.gif` |
| PRE-3 | T-1 | **Countdown / differentiator** — "24h. real USDC, provably fair, instant payouts, no token." | date/countdown + ≥2 trust points | `monk-coin-main.png` |
| PRE-4 (opt) | T-1 | **Mode tease + chat prompt** — "pick your table" + "drop your first pick in the chat 👇" (seed engagement / a poll). | ≥1 mode named + a chat/poll prompt | `cards/beastmode.gif` |

Also specify the **group description + pinned welcome + pinned safety notice** go live at **T-2** (deliverable §1),
and that the mod bot + captcha are configured **before** any invites (§8.5).

### 7.2 Launch-day sequence — T-0 (2026-07-23), **≥5 messages** (post in order; **pin T0-1**)

| # | Purpose | Must include |
| --- | --- | --- |
| T0-1 | **We're live** announce — **PIN THIS**. | "live now" + one-liner; `[PLAY_URL]` in body + **PLAY NOW** button |
| T0-2 | **What it is** — infinite monkey theorem + Edson, one-liner. | core mechanic; USDC on Solana |
| T0-3 | **How a round works** — 3 steps: call your pick → back it with USDC + pull the lever → Edson types live, first exact pick sweeps the pot. | the 3 steps |
| T0-4 | **The 3 modes** — QUICK RACES / BEAST MODE / LOTTERIES (use §5.2 framing). | all 3 modes |
| T0-5 | **Why it's fair** — provably-fair per-round seed, instant on-chain Solana payouts, auto-refunds; **no house coin, no presale — "No token. Yet."** + anti-scam ("no CA in here — that's a scammer"). | 3 trust points + token-free + anti-scam line |
| T0-6 | **CTA + who we are** — "3 friends built this, come play"; "Join the gang". | CTA + **PLAY NOW** button / `[PLAY_URL]` |

### 7.3 First-week sustain — T+1 … T+7, **≥5 messages**

| Slot | Day (suggested) | Angle | Media |
| --- | --- | --- | --- |
| POST-1 | T+1 | **Edson lore drop** (6 beats → a short series or one formatted message). | `backstory/part1..6.png` |
| POST-2 | T+2 | **BEAST MODE spotlight** (rabbit/monkey/jaguar/falcon, no typing). | `cards/beastmode.gif` |
| POST-3 | T+3 | **LOTTERIES spotlight** (draw every 4h + daily; house seeds the pot). | `cards/c.png` |
| POST-4 | T+4 | **Provably-fair explainer** — how to verify a round (point to the in-app verifier). | `game/keyboard.png` |
| POST-5 | T+5 | **Engagement — a native Telegram poll** ("what does Edson type next?" / "which beast wins?"). | `game/idle.gif` |
| POST-6 | T+6–7 | **Real social proof _if it exists_** (first winners / biggest pot) — **real numbers only**; else reframe as an invite. | `edson.png` (or a real winning-round screenshot) |

> **Telegram-native mechanics to prefer** (the implementer should use these instead of X-isms): **pinned** launch
> message; **inline PLAY NOW button**; **native polls** (POST-5); **per-joiner welcome message** (auto, via the bot);
> emoji **reactions**; **@admin** tags for support. These are engagement levers X doesn't have — lean on them.

---

## 8. Organic growth playbook (required subsections of deliverable §4)

$0 budget. The implementer must produce concrete, **currently-real** tactics (research at implement time via web
search where "currently-real" is needed — named communities/handles change and must be verified against
impersonators). Required subsections:

1. **Group setup & optimisation (pointer to §6/§8.5)** — public group; claim `[GROUP_URL]`; the ≤255-char
   description with the one-liner; group photo (`edson.png`); the **pinned welcome** (with PLAY NOW button) and the
   **pinned safety notice**; mod bot + captcha configured. A cold visitor must "get it" in 2 seconds: *bet USDC on
   what a monkey types, provably fair, on Solana.*
2. **Cold-start seeding (near-zero members).** Never invite anyone into an empty, silent group. First-hour list: the
   3 crew (Bidi, Ryugaki, Bolso) + close friends join, say gm, seed a few real messages and a poll, so the first
   real visitor sees a living room. Set the **welcome message** so every joiner is greeted + pointed at the pins.
3. **X → Telegram funnel (the cross-platform spine — cross-reference #76).** Twitter/X is top-of-funnel reach;
   Telegram is where they land and stay. Concrete tactics: put `[GROUP_URL]` in the **X bio** and the **pinned X
   thread**; add a "join the gang on Telegram 👉 `[GROUP_URL]`" **first-reply** to the X launch thread (T0-6); mirror
   each X announcement into the group; wire the **LP `TELEGRAM` social link** (currently a `#` placeholder in
   `monkify-lp/src/pages/index.astro`, `TODO(ship)`) to `[GROUP_URL]` — *that LP edit is a separate task, noted, not
   done in this card.* Note the funnel is **additive**: if the X campaign slips, the Telegram plan still runs.
4. **Where the audience is (verify every handle — impersonation is rife).** Name concrete **Solana** Telegram venues
   to show up in: Solana degen/gaming/casino community groups, on-chain-gaming hubs, and **group directories**
   (e.g. TGStat / Telemetr-style catalogs) to find active Solana groups. Build a short list of 10–20 groups to be
   genuinely active in. *(Implementer fills current, real names/links — do not ship stale placeholders; verify each
   is real + active, not a look-alike.)*
5. **Cross-promotion / partner shoutouts ($0).** Swap community shoutouts / "friendly project" intros with other
   **small** Solana project groups (mutual, unpaid). Never buy a shill; never fake a partnership.
6. **In-group engagement mechanics (Telegram-native).** Daily prompts, **polls** ("call Edson's next run"), "gm",
   reaction games, lore drops, mode clips, pinned "round of the day". Keep an admin present in active hours — a
   community lives or dies on replies, not announcements.
7. **Best times / cadence.** Crypto Telegram is busiest around the US/EU overlap; but a group is 24/7 — the goal is
   **consistent daily activity**, not a single post. Give concrete windows for the *announcements* and a "keep the
   chat warm" note.
8. **Free KOL / group-admin outreach (no paid promo).** A short, honest DM offering early access / a look to **small**
   Solana-gaming creators and **friendly group admins** (who can drop a genuine shoutout). Never promise payment you
   can't make; never fake a partnership.
9. **Ongoing content cadence.** Beyond launch week: memes, Edson lore, polls, "gm", real winner reposts (real only).
   Keep it a loop, not a one-off.
10. **Precedent scan (research).** 3–5 concrete lessons from how comparable **Solana** betting/arcade/degen apps ran
    their **Telegram** launch — e.g. the near-universal **captcha/anti-raid bot** (Safeguard-style) on entry,
    announcement-channel-vs-single-group choices, community-first/lore-first growth, and raid/scam realities. Card
    asks for this explicitly.
11. **Platform-safety notes.** Telegram permits links (so buttons/pins are fine), but the real risk is **scams inside
    the group** — impersonation, fake CAs, DM phishing (covered operationally in §8.5). Plus a one-line
    responsible-gaming reminder and the token-free/anti-scam rule.

### 8.5 Group setup, moderation & anti-scam blueprint (deliverable §6 — Telegram-specific)

This is the section the Twitter campaign has no equivalent of. Crypto Telegram groups are **primary scam targets**
(impersonator "admins" DMing members, fake contract addresses, seed-phrase phishing, bot raids), so a safe group is
part of "done". The implementer writes deliverable §6 covering:

1. **Group creation & setup checklist.** Create a **public group** (so it has a `t.me/[GROUP_URL]` link); set the
   **display name** "Space Stroker" and a **group photo** (`edson.png`); write the **≤255-char description**
   (bio-equivalent — the one-liner + "No token." + `[PLAY_URL]`); enable **reactions**; consider **slow mode** for
   raids; restrict **new members** from posting links/media/forwards for their first hours (anti-spam). Claim the
   username early so nobody squats it.
2. **Moderation bot stack.** Name a concrete, currently-real stack (verify at implement time): a **group-management
   bot** (e.g. **Rose** `@MissRose_bot`, or **Combot**) for welcome message, `/rules`, warns/bans, word **filters**
   and flood control; **plus a captcha / anti-raid bot** (e.g. **Safeguard**-style join verification) — the
   crypto-Telegram norm to stop bot-account raids at the door. The bot token is a **placeholder `[BOT_TOKEN]`** in
   the doc — never a real one (`constraints.md` §1).
3. **The PLAY NOW button, delivered.** An inline URL button must be **posted by a bot**. Cleanest path: configure the
   management bot's **welcome message** and the **pinned launch message** with a `🎮 PLAY NOW → [PLAY_URL]` inline URL
   button (Rose/most bots support inline URL buttons in welcome/notes/pins). **Plain-link fallback:** if the team
   doesn't run a bot, put the raw `[PLAY_URL]` in the message/pin text — Telegram auto-links it (tappable). **Ordinary
   members cannot post inline buttons**, so member-facing copy always uses the plain link. State both in the doc.
4. **Ready-to-pin anti-scam safety notice** (paste-ready, pinned at T-2). Must include, at minimum:
   - "**Admins will NEVER DM you first.** Anyone who DMs you claiming to be the team is a scammer — report + block."
   - "**There is NO token and NO contract address.** Anyone posting a CA or ticker in here is a scammer. Report it;
     it will be removed and banned." (ties to the token-free stance)
   - "**We will never ask for your seed phrase, private key, or to 'connect'/'validate' your wallet on a link.**"
   - "**Only these links are real:** `[PLAY_URL]` (play) and `[GROUP_URL]` (this group). Everything else is fake."
   - who the **real admins** are (pin the admin list / usernames), and how to report a fake.
5. **Automated defences.** Configure the bot to **auto-remove / warn** on scam keywords and patterns (e.g. "airdrop",
   "claim", "connect wallet", "DM me", "verify your wallet", non-admin `t.me/`/URL posts from new members), enable
   **captcha on join**, **flood/anti-spam**, and restrict forwards from channels. Keep it tuned so it doesn't nuke
   normal chat.
6. **Content guardrails + pre-post self-audit.** Restate the §6 hard guardrails (no ROI/guaranteed, no fabricated
   metrics, no ticker/CA, responsible-gaming) and give a **grep self-audit** the user runs before posting a batch
   (see §11). This keeps the *team's own* copy honest, separate from moderating *members*.

---

## 9. Out of scope

- **Twitter/X** campaign — separate card **#76** ([gApr5z7v](https://trello.com/c/gApr5z7v)); referenced for the
  funnel (§8.3), **not** re-authored here.
- **Discord** and any non-Telegram channel.
- **A separate broadcast channel + discussion group** — the presence model is **one community group** (decision 3).
- **Any paid advertising / promotion** (campaign is $0 organic).
- **Any giveaway / prize / free-credit mechanic.**
- **Creating new images / GIFs / video, or a custom Telegram sticker pack** (media direction **references existing
  assets only**).
- **LP or game-app code changes** — including wiring the LP `TELEGRAM` link to `[GROUP_URL]` (noted as a separate
  task in §8.3, not done here).
- **Actually creating the group / bots / posting the messages** and running the community — this card produces the
  content + plan; setup and posting are the user's action.
- **Standing up a real moderation bot / writing a bot** — the doc *specifies* the bot stack + config; it does not
  build or host a bot. No real bot token is written (`[BOT_TOKEN]` placeholder only).
- **Securing the production app domain / URL and the group username** — copy stays link-agnostic via `[PLAY_URL]` /
  `[GROUP_URL]`.
- **Backend integration test + a `features/<slug>.md` doc** — waived (Minor change, content-only).

---

## 10. File checklist

### 10.1 Backend (`monkify-backend`)

**None** — no code change.

### 10.2 Frontend (`monkify-front` / `monkify-lp`)

**None** — read-only source of product facts + assets (§5). No edits. (Wiring the LP `TELEGRAM` social link to
`[GROUP_URL]` is noted as a **separate** downstream task, not part of this card.)

### 10.3 Meta (`monkify-meta`)

| # | File | Change |
| --- | --- | --- |
| 1 | `marketing/telegram-launch-campaign.md` | **NEW** — the campaign deliverable (§4 structure, 6 required sections). *Created by `/monkify-implement-feature`.* |
| 2 | `specs/telegram-marketing-campaign.md` | **NEW** — this spec. *Created by this skill.* |
| 3 | `INDEX.md` | Add the spec row + "Feature currently in flight" entry. *Updated by this skill.* |

---

## 11. Verification checklist (replaces "unit tests" — content QA)

No test project applies. Instead, `/monkify-implement-feature` verifies the deliverable against these auditable
checks (each maps to an acceptance criterion in §12):

| Check | How | AC |
| --- | --- | --- |
| File + 6 sections present | Open `marketing/telegram-launch-campaign.md`; confirm the six sections: group description + pinned welcome / message copy / schedule / playbook / media direction / group setup + moderation & anti-scam. | 1 |
| Description + welcome on-message, clean | Read the description + pinned welcome; confirm token-free USDC/Solana framing, single-community-group framing, no ticker/CA, no guaranteed-returns. | 2 |
| Message counts | Count ≥3 pre-launch, ≥5 launch-sequence, ≥5 post-launch messages, each paste-ready. | 3 |
| Link + button discipline | Confirm `[PLAY_URL]` appears in body **and** as a PLAY NOW button **and** in the pin; button delivery (bot / admin-pin) + plain-link fallback stated; no hard-coded domain. | 4 |
| Relative-day schedule | Confirm the schedule table keys every message to T-2…T+7, not fixed dates. | 5 |
| Playbook concreteness | Confirm cold-start tactics + named Solana communities/hubs + **X→Telegram funnel (cross-ref #76)** + best times + free KOL/admin outreach + the **precedent scan**. | 6 |
| Moderation/anti-scam section | Confirm a mod-bot stack (Rose/Combot), join captcha/anti-bot, and ready-to-pin safety rules incl. "admins never DM first" + "no token/CA". | 7 (setup) |
| Content audit | Grep the whole file for banned terms (guaranteed, risk-free, ROI, "get rich", ticker/`$SPACE`, CA-like strings); confirm anti-scam + responsible-gaming lines exist. | 7/8 |
| Media direction real | Every media reference resolves to a real path under §5.3; no "create new image / sticker pack" asks. | 8 |

**Suggested self-audit command** (run in the `monkify-meta` worktree):
`grep -niE "guarantee|risk[- ]free|\broi\b|get rich|[0-9]+ ?x\b|to the moon|can'?t lose|\$SPACE|airdrop is live|passive income" marketing/telegram-launch-campaign.md`
→ expect no offending matches (mentions inside the *anti-scam* / guardrail lines are acceptable, judged in context).

---

## 12. Acceptance criteria

Copied from the Trello **Acceptance Criteria** checklist (Given / When / Then):

1. Given the deliverable, when opened, then `monkify-meta/marketing/telegram-launch-campaign.md` exists with distinct
   sections for: (1) group description + pinned welcome, (2) ready-to-paste message copy, (3) posting schedule,
   (4) organic growth playbook, (5) per-message media direction, and (6) group setup + moderation & anti-scam.
2. Given the group description + pinned welcome, when read, then they name the product as token-free USDC
   provably-fair Solana betting, present it as a single open community group, and contain no token ticker/contract
   address and no guaranteed-returns claim.
3. Given the message copy, when counted, then it includes at least 3 pre-launch teaser announcements (T-2/T-1), one
   launch-day announcement sequence of at least 5 messages (T-0), and at least 5 first-week post-launch messages
   (T+1..T+7), each paste-ready.
4. Given any message that references the game, when inspected, then the app link is written as the placeholder
   `[PLAY_URL]` (never a hard-coded domain) and appears directly in the body, plus an inline "PLAY NOW" button and
   the pinned message, with the button's delivery method (bot / admin-pinned) stated and a plain-link fallback for
   member messages.
5. Given the posting schedule, when read, then every piece of message copy is mapped to a relative day (T-2 .. T+7)
   rather than a fixed calendar date, so the plan survives a launch-date slip.
6. Given the organic growth playbook, when read, then it lists concrete $0 cold-start tactics (named Solana
   communities/hubs to seed, engagement in larger groups, best post times, free KOL/group-admin outreach) AND
   explicit X-to-Telegram funnel tactics that cross-reference card #76, and it includes a short precedent scan of how
   comparable Solana betting/arcade/degen apps ran their Telegram launch.
7. Given the group setup + moderation & anti-scam section, when read, then it specifies a moderation bot stack (e.g.
   Rose/Combot), join captcha / anti-bot gating, and ready-to-pin anti-scam safety rules including "admins will never
   DM you first" and "there is no token / contract address".
8. Given the per-message media direction, when checked, then every media suggestion (image/GIF/sticker/poll)
   references an existing Edson/arcade asset from monkify-lp or monkify-front (no request to create new
   images/GIFs/video).

> **Ninth audit (cross-cutting, from card constraints):** the whole deliverable contains no financial-return promise,
> no fabricated metrics/engagement, and no token ticker/CA, and it includes a responsible-gaming-aware note + an
> anti-scam ("no token yet") line. (Folded into the §11 content audit; not a separate Trello checklist item.)

---

## 13. Manual QA

1. Open `marketing/telegram-launch-campaign.md`; skim that the 7 sections (§4; six required + housekeeping) exist and
   read cleanly.
2. Count messages: ≥3 pre-launch, ≥5 launch-sequence, ≥5 post-launch. Read 3–4 representative ones for voice + brand.
3. Char-count the group description → ≤255. Confirm the pinned welcome carries (or falls back to) `[PLAY_URL]`.
4. Confirm link handling: `[PLAY_URL]` in-body + PLAY NOW button + pin, with the bot/plain-link fallback explained.
5. Run the §11 self-audit grep → confirm no banned-term matches outside the guardrail/anti-scam lines.
6. Spot-check 3 media references against the real asset paths in §5.3 (e.g. `ls monkify-lp/src/assets/edson-ship-typing.gif`).
7. Confirm the schedule table is relative-day (T-2…T+7) and every message in §2 appears in it.
8. Read §6: is there a real mod-bot stack, a join captcha, and the "admins never DM first" + "no token/CA" pins?
9. Confirm the growth playbook cross-references **#76** and has the X→Telegram funnel + a precedent scan.

---

## 14. Trello / pipeline

- **Source card:** [#77 Create Telegram marketing campaign](https://trello.com/c/LTrjTgdy/77-create-telegram-marketing-campaign)
  — list **Specs created**; the **Implementation** section on the card carries branch + spec + worktree + level.
- **Worktree:** `.worktrees/telegram-marketing-campaign/monkify-meta` · **Branch:** `feature/telegram-marketing-campaign`.
- **Next:** `/monkify-implement-feature Create Telegram marketing campaign` to write the deliverable (**no
  plan-to-code / Docker deploy / integration test** — content-only; verify via §11). Then
  `/monkify-ship-feature Create Telegram marketing campaign` to merge to `main` (**no `features/<slug>.md` doc** —
  Minor change; the KB sync just adds the file + keeps INDEX current).
- **Sibling:** [#76 Create twitter marketing campaign](https://trello.com/c/gApr5z7v) · `specs/twitter-marketing-campaign.md`
  — same product truth + guardrails; this card is the Telegram half of the launch.

---

## 15. Revision log

| Date | Change |
| --- | --- |
| 2026-07-21 | Initial spec from refined card #77. Content + community-ops deliverable (Telegram launch campaign); mirrors the sibling Twitter spec (`specs/twitter-marketing-campaign.md`) and reuses its verified product truth (brand kit, Edson lore, 3 modes, assets), adapted to Telegram's mechanics per the four refinement decisions: single community group, in-body link + inline PLAY NOW button + pin, a 6th group-setup/moderation/anti-scam section, and a Telegram-as-retention-hub / X-funnels-in cross-platform role. Code sections (data model, migrations, unit tests, backend/frontend behaviour) marked N/A. Asset paths verified present; LP `TELEGRAM` social slot confirmed (`index.astro`, `TODO(ship)` `#` placeholder). |
