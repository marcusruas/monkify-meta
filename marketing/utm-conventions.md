# UTM conventions — Space Stroker

> How we tag outbound links so GA4 can tell where a player came from. Written alongside
> `specs/analytics.md` (Trello #101). **Read this before pasting a game link anywhere public.**

Without tags, every visitor from X, Telegram, Reddit and a partner post lands in GA4 as the same
undifferentiated `social` or `direct` bucket — which is precisely the question the launch campaigns
exist to answer. Tagging costs nothing at post time and cannot be reconstructed afterwards.

## The base URL

```
https://app.spacestroker.com
```

The landing page (`https://www.spacestroker.com`) and the app share **one** GA4 property and one
session, so a visitor who lands on a tagged LP link and then clicks "Play now" keeps the original
source all the way through to `bet_placed`. Tag whichever of the two the audience lands on first.

## The three parameters

| Parameter | Meaning | Allowed values |
| --- | --- | --- |
| `utm_source` | **Which platform** the click came from. Fixed vocabulary — never invent one. | `x`, `telegram`, `reddit`, `discord`, `youtube`, `partner` |
| `utm_medium` | **Where on that platform** the link sat. | `bio`, `pinned`, `post`, `reply`, `dm`, `description` |
| `utm_campaign` | **Which push** it belongs to. | `launch` for the launch push; afterwards `<yyyy-mm>-<theme>`, e.g. `2026-09-jackpot` |

Three rules, and they matter more than the vocabulary itself:

1. **Lowercase, always.** GA4 treats `X` and `x` as two different sources, and they never merge back.
2. **Never tag an internal link.** Tagging a link from the LP to the app (or between app pages)
   **starts a new session and overwrites the original attribution** — the campaign that actually won
   the player disappears from the report. Tags go only on links posted *outside* our own sites.
3. **Never invent a source outside the table.** A one-off `utm_source=twitter` sits next to `x`
   forever and quietly splits every number in half. Add to the table here first, then use it.

## Ready-to-paste links

| Placement | URL |
| --- | --- |
| X — bio link | `https://app.spacestroker.com/?utm_source=x&utm_medium=bio&utm_campaign=launch` |
| X — first reply on a launch tweet | `https://app.spacestroker.com/?utm_source=x&utm_medium=reply&utm_campaign=launch` |
| X — ordinary post | `https://app.spacestroker.com/?utm_source=x&utm_medium=post&utm_campaign=launch` |
| Telegram — group description | `https://app.spacestroker.com/?utm_source=telegram&utm_medium=description&utm_campaign=launch` |
| Telegram — pinned welcome + PLAY NOW button | `https://app.spacestroker.com/?utm_source=telegram&utm_medium=pinned&utm_campaign=launch` |
| Telegram — ordinary message | `https://app.spacestroker.com/?utm_source=telegram&utm_medium=post&utm_campaign=launch` |
| Reddit — post body | `https://app.spacestroker.com/?utm_source=reddit&utm_medium=post&utm_campaign=launch` |
| Discord — server post | `https://app.spacestroker.com/?utm_source=discord&utm_medium=post&utm_campaign=launch` |
| A named partner / KOL | `https://app.spacestroker.com/?utm_source=partner&utm_medium=post&utm_campaign=launch` |

For a specific partner worth measuring on its own, append `&utm_content=<handle>` (lowercase, no `@`)
rather than inventing a new `utm_source`.

## Where the numbers show up

GA4 → **Reports → Acquisition → Traffic acquisition**, dimension *Session source / medium*. Because
the LP and the app are one property, add the **Hostname** dimension to separate "read the landing
page" from "opened the game". `cta_play_click` tells you the LP did its job; `bet_placed` tells you
the channel brought someone who actually played.

Related: `specs/analytics.md` (what is measured and what is deliberately not),
`marketing/twitter-launch-campaign.md`, `marketing/telegram-launch-campaign.md`.
