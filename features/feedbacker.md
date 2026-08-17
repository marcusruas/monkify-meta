# Feature — The Feedbacker Program

> Status: **shipped.** Authoritative spec: [`../specs/feedbacker-program.md`](../specs/feedbacker-program.md)
> (merged via `feature/feedbacker-program` on 2026-08-17). Trello #173
> ([lf5uWFrX](https://trello.com/c/lf5uWFrX)).

## What it does

The Feedbacker Program pays players in the project's token for making the game better: finding bugs
at fixed prices, suggesting what to build, and making things about the game. It ships as an
**announcement on two surfaces** — a section on the landing page and a section in the in-app **How to
play** guide — behind the same token gate the coin surfaces use.

⚠️ **It is an announcement, not a system.** Nothing in either repo takes a submission, judges one, or
pays one out. There is no endpoint, no table, no queue, no job. Submissions arrive in the community
Telegram and are judged and paid **by hand, off-app**. In particular the section's
**`SUBMIT ON TELEGRAM · PAID WEEKLY`** line is an **operational commitment on the team that nothing in
code enforces** — do not read it as a scheduled process.

## Core rules

- **Both surfaces are gated on the token flag** — `TOKEN_LIVE` (LP) / `tokenLive` (app), the existing
  fail-closed, build-time mint gate. On a build with no mint the LP section, its nav entry and its
  Terms clause do not render, and the guide section never enters `GUIDE_SECTIONS`. Nothing on either
  surface mentions the programme or a token amount. The reason is not tidiness: the mint-less LP shows
  the **"No token. Yet."** anti-scam panel, and a *"250M set aside for players"* band directly under
  it would contradict the warning it sits beside.
- **`/how-to-play#feedbacker` resolves safely on a mint-less build** — `HowToPlayPage`'s
  `isKnownSection` matches the hash against the live array, so a shared deep link lands at the top of
  the guide rather than throwing on a missing element. Same property `#token` already had.
- **The LP pitches; the guide is canonical.** The LP band carries the figures and links out with
  `Full rules →`; the guide carries the full rules and eligibility prose. There is one long-form
  answer, not two that drift.
- ⚠️ **Every figure is a TOKEN COUNT, and that is a deliberate exception** to the owner's 2026-08-12
  *"don't talk in token units, only in percentages"* rule — scoped to this programme alone. See
  [`token-launch.md`](token-launch.md) §6 before "harmonising" it. The token surfaces keep their
  percentages, `tokensForPercent` stays deleted, and nothing here derives from `TOTAL_SUPPLY`.
- **The figures live in one named block per repo, mirrored** — `FEEDBACKER` in
  `monkify-lp/src/constants.ts` and `monkify-front/src/constants/feedbacker.ts`. ⚠️ **They move
  together**, exactly like `TREASURY_SLICES`: editing one repo and not the other ships two different
  programmes. A season roll is an edit to `currentSeason` and the season budgets — **no markup and no
  Terms edit**.
- **Season-budget bar heights derive from the budgets** (`budget / max × 100`), so a changed figure
  cannot leave the chart drawing a lie. The trailing `AND DOWN` column is decorative and is **not** a
  season anyone can enter.
- ⚠️ **The 250M is not a second pot.** It **is** the 25% `Community & game rewards` slice of
  `TREASURY_SLICES` — the same money the token section two slots above calls `25%`, and the slice that
  also seeds the jackpot and the Telegram drops. The section states the connection in one line;
  without it a reader adds 250M and 25% into 500M. For the same reason the copy says *set aside for
  players* and never *set aside for this programme*.
- **What it pays:** bugs at **fixed** prices (100k cosmetic / 300k a broken flow / 600k money or round
  integrity); ideas **up to 500k** and made things **up to 50k**, both **judged by hand**.
- **Eligibility, stated on both surfaces:** one person, one wallet, **2.5M a season** at most; awards
  are discretionary; paid work must be disclosed; multi-accounting or stolen work is a permanent ban.
- **The Terms carry the enforceable half, without numbers.** `/terms` gains a gated
  `REWARDS FOR FEEDBACK` clause covering discretion, one-person/one-wallet and the permanent ban. ⚠️
  **No amount, cap, tier price or season figure goes in it** — those change every season and that page
  is hand-maintained (it stated a 0.5% house cut for a while after the rate moved to 1%).
- **No Telegram link on either surface.** The LP's socials already sit in `#community` further down
  the same page, and `monkify-front` deliberately gains **no** handle constant — the portal handle
  stays in the four LP locations [`lp.md`](lp.md) documents.
- **Analytics:** the LP's `Full rules →` fires **`feedbacker_click`** with a `source` label
  (`lp_rules`), deliberately **kept out of `cta_play_click`** — see [`lp.md`](lp.md). In the app the
  guide's nav chip fires the existing `trackGuideSectionClick` with a new `section` value; no new
  wiring.

## ⚠️ Open, and it blocks the next LP deploy

**The season budgets are front-loaded; the treasury is not.** The Streamflow lock shipped on
2026-08-14 (see [`token-launch.md`](token-launch.md)) puts **49% of supply — 490M — on a 52-week
weekly release**, and the 250M community slice sits inside it. That means:

| | |
| --- | --- |
| Weekly unlock, whole locked treasury | 490M ÷ 52 = **~9.42M/week** |
| Unlocked during Season 1 (6 weeks) | **~56.5M**, shared by community + marketing + team |
| Season 1's Feedbacker budget alone | **60M** |

So **Season 1's budget exceeds the entire treasury unlock for its own window**, before marketing or
team take anything and before the jackpot seed and Telegram drops that come from the same slice. The
**total** (250M over ~48 weeks of seasons) is fine against a 52-week release; the **front-loading** is
not. Both claims render on the same page, two slots apart, and the lock sits under a *go verify it*
link.

Resolve before the LP is next deployed — by changing the season budgets, by naming a different funding
source, or by confirming the lock's scope excludes this slice. **Do not treat the numbers as settled
because they shipped**; merging to `master` did not publish them.

## Key code touchpoints

**`monkify-lp`** (branch `master`)

- `src/constants.ts` — `FEEDBACKER` + `FeedbackerSeason` (the figures; mirrored — see above)
- `src/components/FeedbackerSection.vue` — the whole section. ⚠️ Rendered with **no `client:*`
  directive**, so it is static HTML with zero JS; that is also why the `scroll-reveal` hydration trap
  documented on the token slot cannot apply here. Adding an interactive control brings it back.
- `src/pages/index.astro` — the `#feedbacker` `ContentSection` in **slot 3**, gated on `TOKEN_LIVE`
- `src/styles/global.css` — `.neb-p7` (the slot's nebula) and `.chip--magenta`
- `src/components/Header.vue` — the gated `Rewards` nav entry, second
- `src/pages/terms.astro` — the gated `REWARDS FOR FEEDBACK` clause
- `src/layouts/Layout.astro` — the `data-track-event` opt-out in the `cta_play_click` listener
- `src/icons.ts` — `caretDown` / `arrowDown` (⚠️ `▾` and `↓` are outside the self-hosted Press Start 2P
  subset and must be drawn; `·` and `–` **are** in range and stay characters)

**`monkify-front`** (branch `main`)

- `src/constants/feedbacker.ts` — the mirror of the LP block
- `src/components/guide/sections/GuideFeedbacker.vue` — the guide section. ⚠️ Uses **`GuideFactList`
  only**: `GuideTip` is capped at one render per page (and is the sole consumer of
  `.arcade-frame--black`), and `GuideRail` is for ordered sequences and demands art per step.
  `GuideRefunds.vue` is the shape it follows.
- `src/constants/guideSections.ts` — `'feedbacker'` in the `GuideSectionId` union and
  `FEEDBACKER_SECTION`, spread after the token section. ⚠️ The id is a **cross-repo URL contract** —
  the LP links to `/how-to-play#feedbacker`, so renaming it breaks a link in another repo.

**`monkify-backend`** — **not affected.** No entity, endpoint, migration, consumer, event or SignalR
contract. The mandatory happy-path integration test is **N/A** for that reason, recorded rather than
skipped (spec §2.1).
