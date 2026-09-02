// EXAMPLE CAMPAIGN — the worked reference, and the smoke test for the toolchain.
//
//   node tools/build.mjs _example
//   node tools/export-png.mjs _example
//   node tools/render.mjs _example
//
// It is deliberately about the part of the product that has been true the longest (a monkey types,
// you bet on what comes out), so it does not go stale between campaigns and can be rebuilt any time
// to check the tools still work.
//
// ⚠️ IT IS AN EXAMPLE, NOT APPROVED COPY. Before any of these words are published, they get the
// same check as any campaign: open what the product actually does and read the lines beside it.
// See references/compliance.md.
//
// A campaign file is the ONE place a person reads to review a campaign. Copy lives here; how it is
// drawn lives in tools/build.mjs. Nobody should have to open a generator to change a headline.

export const campaign = {
  slug: '_example',
  handle: '@SpaceStroker',
  url: 'spacestroker.com',
  // Record decisions that a reader six months from now cannot recover from the copy itself.
  notes: [
    'Settlement is SOL, not USDC — changed 2026-08-20 (features/sol-settlement.md). Any older',
    'piece saying USDC is wrong and must not be reposted.',
    'Token: none live. The app renders "No token. Yet." when the mint env is empty. No ticker,',
    'no contract address, in any piece, until that changes.',
  ],
};

/* ── STILLS ───────────────────────────────────────────────────────────────────
   One idea per piece. If it needs two, it is two pieces.

   `board`  feed | story | square | x16 | thumb  (tools/boards.mjs)
   `accent` amber | cyan | magenta | live | orbit | phosphor — pick the one that MEANS what the
            piece is about, not the one that looks nice (brand/tokens.css).
   `art`    { src, x, y, zoom, veil } — framing is three variables on purpose: where the image
            sits, how far it is zoomed, and the point that stays put while it zooms.
   Body is `sub` OR `chips` OR `stat`, never two of them.

   ⚠️ Write the line breaks by hand with <br>. Then count the lines that render against the <br>
   you wrote: a mismatch means the type is too large for the width and a line wrapped itself
   somewhere ugly. Guessing and letting the browser decide is what makes an export differ from
   the preview you approved. */
export const stills = [
  {
    id: 's1-01-what-it-is',
    board: 'feed',
    accent: 'amber',
    art: { src: 'edson.png', x: '50%', y: '38%', zoom: 1.06, veil: 0.22 },
    kicker: 'The premise',
    headline: 'A monkey types.<br>You bet on<br><em>the word</em>.',
    sub: 'Space Stroker. On Solana.',
    caption:
      'A monkey types. You bet on the word.\n\nEvery round, the typer runs and the characters land where they land. Pick right and the pot splits your way.\n\nProvably fair, settled on-chain.',
    hashtags: ['solana', 'web3gaming', 'onchain', 'crypto', 'solanagaming'],
    when: 'T-2',
  },
  {
    id: 's1-02-fair',
    board: 'square',
    accent: 'cyan',
    kicker: 'Provably fair',
    headline: 'The keys<br><em>do not lie</em>.',
    // Chips replace body copy. Three at most: a chip row is a glance, not a list.
    chips: ['SEEDED PER ROUND', 'VERIFIABLE AFTER', 'ON-CHAIN PAYOUT'],
    caption:
      'Every round is seeded before it runs and checkable after it ends.\n\nYou do not have to trust the result. You can check it.',
    hashtags: ['provablyfair', 'solana', 'onchain', 'web3gaming'],
    when: 'T-1',
  },
  {
    id: 's1-03-x-card',
    // Born 16:9. The X timeline crops a tall image hard enough to eat the mark and half the
    // headline, so this is NOT the feed piece re-cropped (tools/boards.mjs → x16).
    board: 'x16',
    accent: 'amber',
    art: { src: 'space-bg.png', x: '72%', y: '50%', zoom: 1.02, veil: 0.1 },
    kicker: 'Now live',
    headline: 'Bet on what<br>the monkey<br><em>types</em>.',
    sub: 'Solana. Provably fair. Instant payouts.',
    caption: 'Bet on what the monkey types. Live on Solana.',
    hashtags: ['solana', 'web3gaming'],
    when: 'T-0',
  },
];

/* ── VIDEO ────────────────────────────────────────────────────────────────────
   Four to six scenes, two to four seconds each. The first three seconds decide everything: if the
   hook is weak, nothing after it matters.

   Sound is the game's OWN audio, from monkify-front/src/assets/audios. `music` is the bed;
   per-scene `sfx` marks the cuts. See references/brand.md → "Audio" before adding a bed to
   anything published.

   Scene durations are authored here and drive both the picture and the soundtrack, because
   tools/build.mjs computes the absolute scene times once and hands the same numbers to both. */
export const video = {
  id: 'reels-01-what-it-is',
  format: 'reels',
  music: 'game_starting.mp3',
  musicGain: 0.4,
  accent: 'amber',
  scenes: [
    {
      id: 'hook',
      ms: 2600,
      art: { src: 'edson.png', x: '50%', y: '36%', zoom: 1.08, veil: 0.2 },
      kicker: 'Space Stroker',
      headline: 'A monkey<br>is typing.',
      sfx: 'button_press.mp3',
    },
    {
      id: 'premise',
      ms: 2600,
      headline: 'You bet on<br><em>the word</em>.',
      sfx: 'button_press.mp3',
    },
    {
      id: 'how',
      ms: 3200,
      headline: 'Three steps.',
      chips: ['CONNECT A WALLET', 'PICK YOUR WORD', 'PULL THE LEVER'],
      sfx: 'phone_beep.mp3',
    },
    {
      id: 'fair',
      ms: 2800,
      accent: 'cyan',
      headline: 'Seeded before.<br><em>Checkable</em><br>after.',
      sub: 'Provably fair, settled on-chain.',
      sfx: 'button_press.mp3',
    },
    {
      id: 'close',
      ms: 2600,
      art: { src: 'space-bg.png', x: '50%', y: '44%', zoom: 1.05, veil: 0.18 },
      headline: 'Space Stroker.',
      sub: 'On Solana.',
      sfx: 'victory.mp3',
    },
  ],
};
