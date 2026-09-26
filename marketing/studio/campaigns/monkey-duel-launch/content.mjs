// MONKEY DUEL LAUNCH — Space Stroker
//
//   node campaigns/monkey-duel-launch/record.mjs     (films the live screen, mocked; muxes the bed)
//   node tools/captions.mjs monkey-duel-launch       (the posting sheet)
//
// The video is NOT drawn by tools/build.mjs: it is the real duel page, driven by record.mjs, with
// overlay.js drawing the camera moves, captions and cards on top. This file holds every word that
// appears on screen and in the post, so a copy change never means opening the recorder.
//
// ─── WHAT IS SAFE TO SAY (each traced to features/monkey-duel.md) ─────────────
//   · 1v1: two 6-letter words, one typer stream, the first word typed takes the pot.
//   · Two tabs: VS HOUSE (instant, "nobody to wait for" — the app's own line) and OPEN DUELS (a
//     public board of player-posted rooms you can take, or post your own).
//   · Payout: S × (2 − rate) = 1.98 S at the live 2% (the round-6 artifact's 5% is WRONG).
//   · The rival word on VS HOUSE is drawn from the round's seed, committed before any bet, and is
//     reproducible offline once the round ends (Provably fair section).
//   · A genuine race: either word can land first. Never imply the player's word is favoured.
//
// ─── VOCABULARY ───────────────────────────────────────────────────────────────
// Round not game, SOL not USDC, Space Stroker not Monkify, gamble not wager.
// ⚠️ "House": compliance §1 says not to call ourselves one. The APP's tab reads VS HOUSE and that
// stays on screen (it is the product), but no caption of ours says "house" — scene 4 quotes the
// app's own line instead.
//
// ─── THE ROUND IS A MOCK ──────────────────────────────────────────────────────
// Every number on screen is a stand-in (0.05 stake, +0.098, the board's rooms). The win scene
// carries "Example round" in its fine print, and the post text never calls it a real win.

export const campaign = {
  slug: 'monkey-duel-launch',
  handle: '@SpaceStroker',
  url: 'https://app.spacestroker.com',
  notes: [
    'LAUNCH piece for Monkey Duel (#184), shipped 2026-09-25, deploy 2026-09-26. Post once it is live.',
    'Filmed off the LIVE duel page with every request mocked (owner: "use mocks, not a practice round").',
    'Every number on screen is a stand-in; the win scene says "Example round". Never caption it as a real win.',
    'Music: ONE low, constant bed (game_running_1.mp3), no effects on top — owner direction 2026-09-25.',
    'No caption says "house" (compliance §1); the app\'s own VS HOUSE tab label is shown as-is.',
  ],
};

export const stills = [];

export const video = {
  id: 'x-01-monkey-duel',
  format: 'wide',
  music: 'game_running_1.mp3',
  /* LOW. Normalised to this after the mix, so this is the only knob for level (see render.mjs):
     3 dB under the Orbit explainer, which the owner already asked to be turned down. */
  loudness: -20,

  words: { you: 'stroke', rival: 'nebula' }, // rival: one of the shipped 8-word pool
  stake: 0.05,
  endMs: 3600,

  intro: { kick: 'NEW MODE', line: 'Two words. One typer. First one typed wins.' },

  captions: {
    premise: { kick: '1 V 1', head: 'YOUR WORD <em>VS</em> THEIRS', sub: 'Edson types. The first word he spells takes the pot.', accent: '#ea323c' },
    house: { kick: 'VS HOUSE · INSTANT DUEL', head: '<em>NOBODY</em> TO WAIT FOR', accent: '#4ade80' },
    board: { kick: 'OPEN DUELS', head: "OR TAKE A <em>PLAYER'S</em> DUEL", sub: 'Every open room on one board. Or post your own.', accent: '#3d8cff' },
    word: { head: 'PICK ANY <em>6-LETTER</em> WORD', accent: '#ea323c' },
    stake: { head: 'SET YOUR <em>STAKE</em>', sub: 'Both sides put up the same amount.' },
    pull: { head: '<em>PULL.</em>', accent: '#ffa214' },
    splash: { head: 'THE RIVAL WORD IS <em>DRAWN</em>', sub: 'From a seed committed before you bet. Check it after the round.', accent: '#3d8cff' },
    race: { head: 'FIRST WORD TYPED <em>TAKES THE POT</em>', sub: 'A real race. Either word can land first.', accent: '#ffa214' },
    win: { head: 'WINNER TAKES <em>1.98×</em> THE STAKE', fine: 'Pot is both stakes, less a 2% cut · Example round', accent: '#4ade80' },
  },

  end: {
    kick: 'LIVE NOW',
    line: "Duel on the spot, or take a player's open duel.",
    url: 'app.spacestroker.com',
  },

  /* THE X POST. 280 cap, one hashtag, link in the first reply (twitter-launch-campaign.md §4.5/§6). */
  caption: `New mode: Monkey Duel.

1v1. Your word vs theirs. Edson types, and the first word he spells takes the pot.

Duel on the spot with nobody to wait for, or take a player's open duel off the board.

Keep stroking.`,
  hashtags: ['Solana'],
  reply: 'Monkey Duel: app.spacestroker.com',

  /* For the posting sheet only (tools/captions.mjs); record.mjs owns the real timing. */
  scenes: [
    { id: 'hook', ms: 2500, headline: 'NEW MODE · MONKEY DUEL' },
    { id: 'enter', ms: 1500, headline: '(into the machine)' },
    { id: 'premise', ms: 3300, headline: 'YOUR WORD VS THEIRS' },
    { id: 'house', ms: 2900, headline: 'NOBODY TO WAIT FOR' },
    { id: 'board', ms: 4400, headline: "OR TAKE A PLAYER'S DUEL" },
    { id: 'word', ms: 3800, headline: 'PICK ANY 6-LETTER WORD' },
    { id: 'stake', ms: 3400, headline: 'SET YOUR STAKE' },
    { id: 'pull', ms: 1800, headline: 'PULL.' },
    { id: 'splash', ms: 3300, headline: 'THE RIVAL WORD IS DRAWN' },
    { id: 'race', ms: 5500, headline: 'FIRST WORD TYPED TAKES THE POT' },
    { id: 'win', ms: 3300, headline: 'WINNER TAKES 1.98× THE STAKE' },
    { id: 'end', ms: 3600, headline: 'MONKEY DUEL · LIVE NOW' },
  ],
};
