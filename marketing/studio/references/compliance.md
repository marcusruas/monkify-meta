# Compliance guardrails

**These outrank taste, deadline and everything in the other reference files.** If a piece is
prettier or a headline is punchier for breaking one of these, the piece is wrong.

Most of this is already settled in the channel docs' guardrail sections
([X §6](../../twitter-launch-campaign.md), [Telegram §6.6](../../telegram-launch-campaign.md),
[channels §8](../../promotion-channels.md)). Gathered here because it applies to artwork and video
too, and those docs only ever governed text.

---

## 1. Positioning: a Solana dapp, not a licensed operator

Space Stroker is presented as a **web3 application on Solana**. It is **not** presented as a
licensed gambling operator, casino or betting service, because it does not hold a licence.

That constrains vocabulary. Avoid the words a regulated operator uses about itself: "casino",
"betting platform", "operator", "house", "odds of winning", "guaranteed returns", and anything
implying supervision or licensing that does not exist.

⚠️ **"Gamble" is still the right word for the leaderboard**, because it is what the product's own
UI and API say (`features/leaderboards.md`, owner's call). Describing the *action* honestly is not
the same as claiming an operator's status.

## 2. Never promise money

Not in a headline, not in a caption, not in a voiceover.

Banned outright: guaranteed returns, expected profit, "make money", "earn while you play", "risk
free", "can't lose", any implication that playing is an income strategy.

⚠️ **The leaderboards have no prize.** The feature that shipped 2026-09-02 moves no money: no pool,
no season, no payout, no award. A piece implying a prize is describing something that does not
exist. If a prize is ever added, it will be its own card and its own set of facts.

## 3. Never invent a metric

No player counts, no volume figures, no payout totals, no "biggest win ever" — **unless the number
comes from a real, current source you can point at**, and even then read §6 of
[`voice.md`](voice.md) about what printing a number costs.

⚠️ **`GET /leaderboards` and `GET /games/stats` return real figures**, so a piece *can* legitimately
quote one. If you do: date it, screenshot it, and prefer showing the board over transcribing a
number into type, because a transcribed number is frozen and the board is not.

Never dress a scenario as data. The Trello card's weekly-rake table was explicitly labelled
"scenarios, not live data". That distinction must survive into any piece built from it.

## 4. Token: there is none

There is **no live token**. The app renders "No token. Yet." when the mint env is empty, and that
is the current state.

- **No ticker, no contract address, no chart link, no "presale", no "airdrop"** in any piece.
- Do not imply one is coming on a date. `features/token-launch.md` built the UI ahead of time; the
  UI existing is not the token existing.
- ⚠️ **This is an anti-scam measure, not caution.** Fake tokens impersonating a project appear
  precisely in the window before a real one exists. Every piece that stays silent on a ticker makes
  the impersonators easier to spot, and the LP keeps its "No token. Yet." panel in a prime slot for
  exactly this reason.

## 5. Responsible gambling, and self-exclusion

The product has a self-exclusion mechanism and a responsible-gaming page, and it takes them
seriously enough that **self-excluded players are hidden from every leaderboard while their
exclusion is active** — because putting someone's name on a gambling leaderboard after they
formally asked to be kept away uses their identity to promote what they opted out of
(`features/leaderboards.md`, `features/legal.md`).

Marketing inherits the same standard:

- Never frame playing as a solution to money problems, and never target anyone who looks like they
  have one.
- Never mock a losing player, and never build a piece around someone else's loss.
- ⚠️ **Never name or picture a real player without their agreement**, even from a public
  leaderboard. A public ranking is not consent to appear in an advertisement. Truncated wallet
  addresses are the safe default; a display name is a person.
- Age-restricted product: nothing that would read as aimed at minors. No cartoon-for-kids framing,
  no school references, no "anyone can play".

## 6. Other people's marks

Solana's logo, wallet logos (Phantom, Solflare), exchange logos, and platform logos each have their
own usage rules. Do not redraw them, recolour them, or pixel-snap them into our style.

The app already makes this call: someone else's logo renders with `image-rendering: auto` because
"pixel-snapping someone else's logo is not our call" (`OrbitStatStrip.vue`). Same rule here.

Naming a chain in text ("on Solana") is fine. Putting a third party's mark on a piece implies an
endorsement, so it needs a reason.

## 7. Before anything is published

Run through this. It takes two minutes and it is shorter than a retraction.

- [ ] Every claim traced to `features/` or `specs/`, not to a Trello card or a memory.
- [ ] No prize, pool or payout implied where none exists.
- [ ] No invented number; every real number dated and sourced.
- [ ] No ticker, contract address or token timing.
- [ ] No guaranteed return, no "earn", no "risk free".
- [ ] No real player named or pictured without agreement.
- [ ] No third-party logo without a reason.
- [ ] Currency is **SOL**, and the word is **gamble**.
- [ ] Safe zones checked with `tools/proof.mjs` for the surface it is going to.
- [ ] Exported at nominal size (the exporter enforces this — do not use `--allow-wrap` to silence it).
