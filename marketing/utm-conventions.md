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

The landing page (`https://www.spacestroker.com`) and the app share **one** GA4 property, so a
visitor who lands on a tagged LP link and then clicks "Play now" keeps the original source all the
way through to `bet_placed`. Tag whichever of the two the audience lands on first.

> ⚠️ **That sentence used to say "one property and one session", and the session half was wrong** —
> it cost the first paid campaign (Bitmedia, Aug 2026) its entire attribution. The two hosts share a
> registrable domain and therefore a `_ga` cookie, but Consent Mode defaults `analytics_storage` to
> **denied**, so for every visitor who ignores the consent banner that cookie is never written and
> there is no session to continue. The hop then starts a fresh, sourceless session and the ad shows up
> as `(direct) / (none)`. What actually carries the campaign now is `Layout.astro`, which copies the
> `utm_*` parameters from the LP's own URL onto every `GAME_URL` link at click time. Attribution
> across the hop is a **mechanism in the LP**, not a property of GA4 — if that script is removed, LP
> landings stop being attributable again.

## The three parameters

| Parameter | Meaning | Allowed values |
| --- | --- | --- |
| `utm_source` | **Which platform** the click came from. Fixed vocabulary — never invent one. | `x`, `telegram`, `reddit`, `discord`, `youtube`, `partner`, `bitmedia` |
| `utm_medium` | **Where on that platform** the link sat. | `bio`, `pinned`, `post`, `reply`, `dm`, `description`, `banner` |
| `utm_campaign` | **Which push** it belongs to. | `launch` for the launch push; afterwards `<yyyy-mm>-<theme>`, e.g. `2026-09-jackpot` |

Three rules, and they matter more than the vocabulary itself:

1. **Lowercase, always.** GA4 treats `X` and `x` as two different sources, and they never merge back.
2. **Never tag an internal link with a NEW campaign.** Writing a fresh `utm_source` onto a link from
   the LP to the app (or between app pages) **starts a new session and overwrites the original
   attribution** — the campaign that actually won the player disappears from the report. Hand-written
   tags go only on links posted *outside* our own sites.
   **This is not the same as the LP's automatic forwarding**, which copies the incoming values through
   **unchanged**. Copying cannot overwrite a source with a different one; the worst it can do is open a
   second session already attributed to the same campaign — a slightly inflated session count, in
   exchange for attribution that would otherwise not exist at all. Never "fix" that forwarder by
   substituting values of your own; the moment it rewrites rather than copies, this rule is broken.
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
| Bitmedia display banner | `https://www.spacestroker.com/?utm_source=bitmedia&utm_medium=banner&utm_campaign=<yyyy-mm>-<run>` |

For a specific partner worth measuring on its own, append `&utm_content=<handle>` (lowercase, no `@`)
rather than inventing a new `utm_source`.

### Paid traffic is the exception that points at the LP

Every row above targets `app.` because the audience already knows what Space Stroker is — they follow
the account or read the post the link sat in. **Bitmedia is the one that targets `www.`**: a banner
buys a stranger who has seen one 300×250 image, and dropping them straight into a live game UI with no
explanation is what a bounce is made of. That is the whole reason the LP→app forwarding above has to
work — the paid funnel is two hosts long, and every other channel's is one.

Use one `utm_campaign` per **run**, not per creative (`2026-08-first-run`), and put the creative in
`utm_content` if a banner ever needs measuring on its own. `bitmedia` is the first **paid** source in
the table; the rest are organic, so any report that mixes them without splitting on source is
comparing bought traffic to earned traffic.

## Where the numbers show up

GA4 → **Reports → Acquisition → Traffic acquisition**, dimension *Session source / medium*. Because
the LP and the app are one property, add the **Hostname** dimension to separate "read the landing
page" from "opened the game". `cta_play_click` tells you the LP did its job; `bet_placed` tells you
the channel brought someone who actually played.

Related: `specs/analytics.md` (what is measured and what is deliberately not),
`marketing/twitter-launch-campaign.md`, `marketing/telegram-launch-campaign.md`.
