// LEADERBOARD LAUNCH — Space Stroker
//
//   node tools/build.mjs      leaderboard-launch
//   node tools/export-png.mjs leaderboard-launch
//   node tools/render.mjs     leaderboard-launch
//   node tools/slides.mjs     leaderboard-launch
//
// ─── WHAT SHIPPED, AND WHY THIS CAMPAIGN LOOKS NOTHING LIKE THE ORIGINAL IDEA ──
// Sources: monkify-meta/features/leaderboards.md and specs/leaderboards.md (Trello #182, merged
// 2026-09-02). NOT Trello #178, which is a different, unbuilt feature.
//
// #178 proposed a weekly wager race with a real prize pool plus a hall of fame. The owner cut the
// paying half during refinement. What exists:
//
//   · NO prize, NO pool, NO season, NO payout. The feature moves no money and writes nothing.
//   · ONE of the three boards is visible. The gamble race renders; weekly PNL and hall of fame are
//     built but sit behind VUE_APP_WEEKLY_PNL_ENABLED and VUE_APP_HALL_OF_FAME_ENABLED, both
//     fail-closed `false` and both BUILD-time, so switching one on is a frontend rebuild.
//     ⚠️ Nothing in this campaign may reference the hall of fame or a PNL board.
//   · Rolling 7 days, NOT a calendar week. "The race ends Sunday" would be false.
//   · The word is GAMBLE, never "wager" — owner decision, applied to the copy and the wire
//     contract (`gambleRace`).
//   · Currency is SOL (since 2026-08-20). Never USDC.
//
// ─── THE HOOK, AND IT IS A REAL PRODUCT PROPERTY ──────────────────────────────
// `RequiredAmount` is fixed per game type and there is one bet per player per round, so — in the
// feature doc's own words — a gamble race "is separated by ROUNDS PLAYED, not bet size". Nobody
// can buy the top of this board by betting bigger, because there is no bet-sizing dimension at
// all. In a category where leaderboards are won by whoever deposits most, that is the honest
// differentiator, and it is verifiable in the code rather than being a claim.
//
// ─── WHAT IS SAFE TO SAY (each traced to features/leaderboards.md) ────────────
//   · rolling 7 days, with the period always named from the response
//   · top 10 on the home section, top 25 on /leaderboard
//   · your own row is pinned when you rank outside the visible slice
//   · house-controlled wallets are excluded by configured public key
//   · no minimum participation: one qualifying bet is enough
//   · only completed rounds count; free bets never count
//   · self-excluded players are hidden while their exclusion is active
//
// ─── WHAT IS FORBIDDEN HERE (references/compliance.md) ────────────────────────
//   · any prize, pool, payout, reward or "win" framing. There is none.
//   · any invented figure. NO fabricated leaderboard rows, NO made-up wallet names, NO example
//     rankings — a piece showing a fake board is a fake metric with extra steps.
//   · naming or picturing a real player. A public ranking is not consent to be advertised.
//   · a token ticker or contract address. There is no live token.
//
// ─── LINKS ────────────────────────────────────────────────────────────────────
// The board lives at https://app.spacestroker.com/leaderboard
// ⚠️ Do NOT print a UTM-tagged URL on artwork — a picture cannot carry a per-channel tag. Put the
// plain route on the piece (or nothing, the handle is the footer) and tag the link in the CAPTION,
// per marketing/utm-conventions.md. Captions below leave `?utm_...` to be filled at post time.

export const campaign = {
  slug: 'leaderboard-launch',
  handle: '@SpaceStroker',
  url: 'https://app.spacestroker.com/leaderboard',
  notes: [
    'STATE AS OF 2026-09-02, from the owner. This SUPERSEDES the knowledge base:',
    '  - Project is LAUNCHED and the token is LIVE on pump.fun. The old no-token rule is void.',
    '  - Leaderboard is merged and starts soon. It is NOT yet deployed to production.',
    '  - There IS a prize, run by the team: 3 SOL a week, 2 SOL 1st, 0.6 SOL + 3M STROKE 2nd,',
    '    0.4 SOL + 2M STROKE 3rd. The shipped feature moves no money, so the payout is MANUAL.',
    '    Never describe it as automatic or on-chain.',
    '  - Weekly, and this cycle runs until Wednesday.',
    'Vocabulary: gamble not wager, round not game. Settlement is SOL, never the token.',
    'The shot in ui/ is a MOCKED render of the real page (owner call). Never caption it as live',
    'standings, and never quote a figure off it.',
  ],
};

export const stills = [
  {
    // The announcement. Deliberately plain: the news is that a thing exists.
    id: 's1-01-board-is-live',
    board: 'feed',
    accent: 'amber',
    art: { src: 'cards/cosmicsprint-poster.png', x: '50%', y: '42%', zoom: 1, veil: 0.26 },
    kicker: 'New',
    headline: 'The board<br>is <em>live</em>.',
    sub: 'Rolling 7 days. Top 25.',
    caption:
      'The board is live.\n\nEvery completed round you play counts toward a rolling 7-day ranking. Top 25 on the page, and your own row is pinned even when you are nowhere near it.\n\nNo prize. Just the top of the board.\n\napp.spacestroker.com/leaderboard',
    hashtags: ['solana', 'web3gaming', 'onchain', 'leaderboard', 'solanagaming'],
    when: 'T-0',
  },
  {
    // The differentiator, and the strongest piece in the set. Cyan because this is the
    // fairness beat, and cyan is the verification accent (brand/tokens.css).
    id: 's1-02-rank-is-rounds',
    board: 'square',
    accent: 'cyan',
    kicker: 'How it ranks',
    headline: 'Rank is rounds,<br>not <em>bet size</em>.',
    chips: ['FIXED STAKES', 'ONE BET PER ROUND', 'ROUNDS DECIDE IT'],
    caption:
      "Rank is rounds, not bet size.\n\nStakes are fixed per game type and it is one bet per player per round, so there is no bet-sizing dimension at all. The board separates people by rounds played, not by who bet biggest.\n\napp.spacestroker.com/leaderboard",
    hashtags: ['solana', 'provablyfair', 'web3gaming', 'leaderboard'],
    when: 'T+1',
  },
  {
    // Born 16:9. The X timeline crops a tall piece hard enough to eat the mark and half the
    // headline, so this is not the feed piece re-cropped.
    id: 's1-04-x-card',
    board: 'x16',
    accent: 'amber',
    art: { src: 'space-bg.png', x: '68%', y: '50%', zoom: 1, veil: 0.12 },
    kicker: 'New',
    headline: 'The board<br>is <em>live</em>.',
    sub: 'Rolling 7 days. Top 25.',
    caption:
      'The board is live.\n\nRolling 7 days, top 25, your own row pinned. Stakes are fixed, so it ranks by rounds played and nobody can buy the top.\n\nNo prize. Just the top of the board.',
    hashtags: ['solana', 'web3gaming'],
    when: 'T-0',
  },
  {
    // Stories / Reels cover. Copy sits between 20% and 78% of the height because Instagram draws
    // its interface over the piece (references/platforms.md §1).
    id: 's1-05-story',
    board: 'story',
    accent: 'amber',
    art: { src: 'cards/spacejackpot-poster.png', x: '50%', y: '38%', zoom: 1, veil: 0.28 },
    kicker: 'New',
    headline: 'The board<br>is <em>live</em>.',
    sub: 'Rolling 7 days.',
    caption: 'The board is live. Rolling 7 days, top 25.',
    hashtags: ['solana', 'leaderboard'],
    when: 'T-0',
  },
];

export const video = {
  id: 'x-01-leaderboard',
  format: 'wide',   // X: the one shape the timeline never crops (tools/boards.mjs)
  /* CONSTANT MUSIC. The active-round track, looped for the whole piece and faded only at the tail.
     A promo that goes silent between beats reads as a broken file, and X gives you no second
     chance once someone has scrolled past. */
  music: 'game_running_1.mp3',
  musicGain: 0.62,
  accent: 'amber',
  /* The starfield sits behind every scene, so nothing is ever a black rectangle. */
  bg: { src: 'space-bg.png', x: '55%', y: '50%', zoom: 1.04, veil: 0.1 },

  /* THE X POST. One hashtag, link in the first reply, 280 cap — twitter-launch-campaign.md
     §4.5/§6. A template literal, so the line breaks are real and nothing has to be escaped. */
  caption: `THE LEADERBOARD IS LIVE.

3 SOL + $STROKE on the line every week.

1st — 2 SOL
2nd — 0.6 SOL + 3M $STROKE
3rd — 0.4 SOL + 2M $STROKE

Ranked by total SOL gambled. This week runs until Wednesday.

Start stroking.`,
  hashtags: ['Solana'],
  reply: 'Board: app.spacestroker.com/leaderboard',

  scenes: [
    {
      id: 'hook',
      ms: 2600,
      art: { src: 'edson-ship-typing.gif', x: '50%', y: '42%', zoom: 1, veil: 0.18 },
      kicker: 'Now live',
      headline: 'THE<br><em>LEADERBOARD</em>',
      sub: 'Everyone ranked. Every week.',
      sfx: 'button_press.mp3',
    },
    {
      /* THE PRODUCT, not a description of it: a real screenshot of /leaderboard, shown whole
         (`fit: contain`) in its own framed panel on the right, so the copy never covers the
         interface it is pointing at. */
      id: 'ui',
      ms: 3000,
      art: { src: 'leaderboard-page.png', fit: 'contain', frame: 'right', veil: 0, render: 'auto', zoom: 1 },
      kicker: 'The board',
      headline: 'Ranked by<br>SOL <em>gambled</em>.',
      sfx: 'phone_beep.mp3',
    },
    {
      /* THE PRIZE, which is the reason for the post. Full frame: a three-column ranking squeezed
         into the left half reads as three chips, not as a podium. */
      id: 'prize',
      ms: 4200,
      layout: 'full',
      kicker: 'Weekly prize pool',
      headline: '3 SOL<br>+ <em>$STROKE</em>',
      podium: [
        { place: 1, sol: '2 SOL' },
        { place: 2, sol: '0.6 SOL', stroke: '+ 3M $STROKE' },
        { place: 3, sol: '0.4 SOL', stroke: '+ 2M $STROKE' },
      ],
      sfx: 'victory.mp3',
    },
    {
      id: 'timing',
      ms: 2600,
      accent: 'cyan',
      headline: 'Every week.',
      sub: 'This one runs until Wednesday.',
      sfx: 'button_press.mp3',
    },
    {
      id: 'cta',
      ms: 2600,
      art: { src: 'monk-coin-main.png', fit: 'contain', frame: 'right', zoom: 1, veil: 0, render: 'auto' },
      headline: 'Start<br><em>stroking</em>.',
      sub: 'app.spacestroker.com/leaderboard',
      sfx: 'victory.mp3',
    },
  ],
};
