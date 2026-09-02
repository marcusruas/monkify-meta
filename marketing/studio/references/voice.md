# Voice

How Space Stroker sounds. Applies to every word that reaches a piece, a caption or a script.

The channel docs already settled a house voice in practice — read
[`../../twitter-launch-campaign.md`](../../twitter-launch-campaign.md) and
[`../../telegram-launch-campaign.md`](../../telegram-launch-campaign.md) before writing a campaign,
because their guardrail sections are decisions already taken, not suggestions. This file is the
part that generalises.

---

## 1. Check the product before you check the tone

This is the expensive mistake, and it disguises itself as a style problem.

Open what the product actually does — the feature doc, the spec, the config, the running app — and
read the campaign beside it. If the campaign promises one thing and the product does another, no
amount of tonal polish fixes it: everyone who arrives because of the promise finds something else.

It is not hypothetical here. In **August 2026** a Trello card described a leaderboard as a *weekly
wager race with real prizes* plus a hall of fame. What shipped on **2026-09-02** has **no prize, no
pool, no payout — it moves no money at all**, the owner having cut the paying half during
refinement. A campaign written from the card would have advertised prizes that do not exist.

The rule that falls out:

> **Write from `features/` and `specs/`, never from a card, a memory or a brainstorm.**
> A card is what someone hoped to build. `features/` is what exists.

And re-check before reposting. Settlement moved from **USDC to SOL** on 2026-08-20, so any older
piece naming USDC is now false.

## 2. Say it straight

A company describing itself clearly and confidently. Not a marketing department, and not a brand
trying to sound young.

There are two ways to get this wrong, and the second is the one people walk into:

| Failure | How it reads |
| --- | --- |
| Too formal | An HR memo. Nobody finishes it. |
| **A brand trying to be cool** | Worse: forced slang, the joke explained, "we don't judge". It signals that the company is not sure what it built. |

Both have the same exit: **write like someone who knows what they made.** That voice ages well.
Streamer slang does not.

**If the thing is already funny, the copy does not need to announce it.** A monkey typing random
characters for money is absurd on its own. A straight voice presenting an absurd thing lands better
than slang nudging you about the joke — signalling it twice is precisely what reads as trying too
hard. Let the product be funny and let the text be exact.

**Concrete beats abstract.** "Seeded before the round, checkable after" beats "industry-leading
transparency".

**Cut the filler.** "revolutionary", "seamless", "cutting-edge", "the ultimate", "unleash". If a
sentence survives deleting the adjective, it was not doing anything.

## 3. One thing, one name

The fastest tell that copy was written by several people is the same thing appearing under two
names in one campaign.

Ours are already decided, and some were decided the hard way:

| Say | Not | Why |
| --- | --- | --- |
| **gamble** | wager | The owner replaced "wager" with "gamble" in the leaderboard copy **and in the wire contract** (`features/leaderboards.md`). The UI says gamble; the campaign says gamble. |
| **SOL** | USDC, "crypto", "$" | Settlement is native SOL since 2026-08-20. |
| **round** | game, match, draw | "Game" already means a game *type* in this product. |
| **Space Stroker** | Monkify | The rebrand is done everywhere. Monkify only ever appears as folder names in this repo. |
| **Edson** | "the monkey", "the mascot" | He has a name. Use it once the audience knows it; "a monkey types" is still the right opening line for someone who has never heard of us. |

Pick once, write it down in the campaign's `content.mjs`, and keep it for the whole campaign.

**English only, and no code-switching mid-headline.** These are English-language channels. A
foreign word the audience genuinely uses can enter, but then it enters everywhere, not in the
headline with a translation in the body.

## 4. Type on a piece

- **Kicker** — small label above the headline, one to three words, sets context.
- **Headline** — the whole idea, three to eight words, enormous. One word may take the accent
  colour. One.
- **Sub, chips, or a stat** — pick exactly one. They replace body copy; they never stack on it.
- **Handle** — always. It is the only way a screenshot stays traceable.

**Balanced headlines.** No orphan word alone on the last line, halves of similar length. Write the
line breaks by hand and count what rendered against the `<br>` you wrote — `tools/checks.mjs` does
this automatically now and fails the export, because the display face is Press Start 2P, whose
every glyph is exactly 1em wide, so a line that fits at sixteen characters breaks at seventeen.

**If the copy does not fit, cut the copy — do not shrink the type.** Type that had to come down to
fit is copy that was too long.

**No exclamation marks, no emoji in the artwork.** The text stands on its own. Emoji are fine in a
Telegram message body, where they are how that platform reads.

## 5. Em dashes

`mkt`'s first rule was to ban the em dash outright. Here that ban applies **to rendered copy on
pieces only** — headlines, subs, chips, captions — where it is a typographic call: at display size a
dash is a big horizontal hole, and in Press Start 2P it is a full-width block.

It does **not** apply to prose documents like this one, or to the rest of the knowledge base, which
uses em dashes throughout. Two different jobs, two different rules.

## 6. Honesty, and what a number costs

**Never invent a figure, a testimonial, a badge or a milestone.** Not a player count, not a payout
total, not "trusted by".

**A number on a piece is a promise, and the piece outlives the decision.** It circulates for months
and nobody goes back to correct a screenshot. Do not print a prize, a date, a rate or a threshold
that is not settled — and if it is settled, it must be verifiable in `features/` or in config.

The way out is almost always available: *"a new board every week"* says the same thing as *"top 10
paid every Monday"* without creating a debt.

⚠️ **This is doubly true here because the product is money.** See
[`compliance.md`](compliance.md), which outranks everything in this file.
