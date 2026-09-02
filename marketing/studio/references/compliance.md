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

Banned outright: guaranteed returns, expected profit, "risk free", "can't lose", any implication
that playing is an income strategy. The line the X guardrails already drew is the right one:
**betting language is fine, outcome promises are not.** "Bet on what he types" is the product;
"you'll win" is a promise nobody can keep.

⚠️ **The leaderboard prize is REAL, and it lives OUTSIDE the code.** As of 2026-09-02 the team runs
a **weekly 3 SOL pool** — 2 SOL first, 0.6 SOL + 3M $STROKE second, 0.4 SOL + 2M $STROKE third.
The *shipped feature* still moves no money (no season table, no payout consumer, no award record),
so the payout is **operational and manual, not a product guarantee**. Two consequences for copy:

- The figures are quotable, because the owner set them and can be held to them.
- Do **not** describe them as automatic, on-chain, or paid out by the platform, and do not imply
  the app enforces them. It is a promotion the team runs. If it changes, every piece naming a
  figure is wrong — re-check before reposting.

## 3. Never invent a metric

No player counts, no volume figures, no payout totals, no "biggest win ever" — **unless the number
comes from a real, current source you can point at**, and even then read §6 of
[`voice.md`](voice.md) about what printing a number costs.

⚠️ **`GET /leaderboards` and `GET /games/stats` return real figures**, so a piece *can* legitimately
quote one. If you do: date it, screenshot it, and prefer showing the board over transcribing a
number into type, because a transcribed number is frozen and the board is not.

Never dress a scenario as data. The Trello card's weekly-rake table was explicitly labelled
"scenarios, not live data". That distinction must survive into any piece built from it.

## 4. Token: $STROKE is live

⚠️ **This section was inverted on 2026-09-02.** It used to say there was no token. There is one:
**$STROKE is live on pump.fun**, and the X bio says so. Anything written against the old rule is
out of date.

What that changes, and what it does not:

- **The ticker may appear.** `$STROKE` in copy and on a piece is fine.
- **A contract address must be verified before it is printed**, and printing one on artwork is
  usually the wrong place for it — an image cannot be copied, and a wrong address in a screenshot
  circulates forever. Put it where it can be checked and corrected.
- ⚠️ **Anti-scam does not relax; it gets more important.** A live token is exactly what
  impersonators clone. Never post an address the owner has not confirmed, and treat any address
  that reaches you through a DM, a reply or a search result as hostile.
- **No price talk, ever.** No targets, no "up only", no market cap milestones, no "x from here",
  no implication that holding is an investment. The token being live does not make it an asset we
  are allowed to promise anything about — §2 still governs.
- **Settlement is still SOL.** The token is not the stake. Do not let a piece imply you bet in
  $STROKE.

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
- [ ] Prize figures match what the owner is actually running this week (3 SOL pool as of 2026-09-02).
- [ ] No invented number; every real number dated and sourced.
- [ ] No unverified contract address, and no price talk of any kind ($STROKE itself is fine).
- [ ] No guaranteed return, no "earn", no "risk free".
- [ ] No real player named or pictured without agreement.
- [ ] No third-party logo without a reason.
- [ ] Currency is **SOL**, and the word is **gamble**.
- [ ] Safe zones checked with `tools/proof.mjs` for the surface it is going to.
- [ ] Exported at nominal size (the exporter enforces this — do not use `--allow-wrap` to silence it).
